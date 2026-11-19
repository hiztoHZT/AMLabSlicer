"""Loopback gRPC adapter for the imported AMLab YOLO detector."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path
import json
import math
import os
import sys
import tempfile
import threading
from uuid import UUID

import grpc
from grpc_tools import protoc


MODEL_ID = "yolo11-emscp-lscd"
MAX_IMAGE_BYTES = 12 * 1024 * 1024
MODEL_RELATIVE_PATH = (
    "runs/train/xin -yolo11-1p2＋emscp-lscd-wEIoUv3-3002/weights/best.pt"
)
PROJECT_RELATIVE_PATH = (
    "YOLO/ultralytics-mokuiyolo11/ultralytics-mokuiyolo11/ultralytics-yolo11-main"
)


def main():
    root = Path(__file__).resolve().parent
    bundled = (root / "amlab_monitor.proto").is_file()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=root if bundled else root.parents[1] / PROJECT_RELATIVE_PATH)
    parser.add_argument("--weights", type=Path)
    parser.add_argument("--port", type=int, default=50120)
    parser.add_argument("--preload", action="store_true")
    parser.add_argument("--device", default="cpu", help="cpu or a CUDA device index")
    args = parser.parse_args()
    project = args.project.resolve()
    default_weights = project / ("models/best.pt" if bundled else MODEL_RELATIVE_PATH)
    weights = (args.weights or default_weights).resolve()
    if not (project / "ultralytics" / "nn" / "extra_modules").is_dir():
        parser.error(f"Cannot find the imported YOLO source at {project}")

    # The checkpoint is an operator-selected full PyTorch model. RPC clients
    # select by ID and cannot supply a path or upload model files.
    os.environ["TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD"] = "1"
    os.environ["YOLO_AUTOINSTALL"] = "false"
    if bundled:
        local_data = Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local")
        os.environ["YOLO_CONFIG_DIR"] = str(local_data / "AMLabSlicer" / "Monitor" / "ultralytics")
    sys.path.insert(0, str(project))
    with tempfile.TemporaryDirectory(prefix="amlab-monitor-grpc-") as generated:
        proto_dir = root if bundled else root.parent / "Protos"
        result = protoc.main([
            "grpc_tools.protoc", f"-I{proto_dir}", f"--python_out={generated}",
            f"--grpc_python_out={generated}", str(proto_dir / "amlab_monitor.proto")
        ])
        if result:
            raise SystemExit(result)
        sys.path.insert(0, generated)
        serve(args.port, args.device, weights, args.preload)


@dataclass(frozen=True)
class ModelEntry:
    id: str
    name: str
    weight_path: Path


def read_models(default_model):
    local_data = Path(os.environ.get("LOCALAPPDATA") or Path.home() / "AppData" / "Local")
    registry = local_data / "AMLabSlicer" / "Monitor" / "models.json"
    models = {default_model.id: default_model}
    try:
        entries = json.loads(registry.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return models

    if not isinstance(entries, list):
        raise ValueError("The vision model configuration must be a JSON array.")
    for entry in entries:
        if not isinstance(entry, dict):
            raise ValueError("Invalid vision model entry.")
        model_id, name, weight_path = (entry.get(key) for key in ("id", "name", "weightPath"))
        if not all(isinstance(value, str) and value.strip() for value in (model_id, name, weight_path)):
            raise ValueError("A vision model is missing its ID, name or weights path.")
        path = Path(weight_path)
        if UUID(model_id).hex != model_id.lower() or model_id in models or not path.is_absolute() or path.suffix.lower() != ".pt":
            raise ValueError("Invalid vision model ID or weights path.")
        models[model_id] = ModelEntry(model_id, name, path)
    return models


def serve(port, device, weights, preload):
    import cv2
    import numpy as np
    from ultralytics import YOLO
    import amlab_monitor_pb2 as pb
    import amlab_monitor_pb2_grpc as rpc

    default_model = ModelEntry(MODEL_ID, "YOLO11 EMSCP-LSCD · best.pt", weights)
    inference_lock = threading.Lock()
    cached_model = None
    cached_signature = None

    def load_checkpoint(path):
        model = YOLO(str(path))
        if model.task != "detect":
            raise ValueError(f"Expected a YOLO detection checkpoint, got {model.task!r}.")
        return model

    if preload:
        info = weights.stat()
        cached_model = load_checkpoint(weights)
        cached_model.predict(np.zeros((640, 640, 3), dtype=np.uint8), imgsz=640,
                             device=device, verbose=False, save=False)
        cached_signature = (weights, info.st_size, info.st_mtime_ns)

    def get_models(context):
        try:
            return read_models(default_model)
        except (OSError, ValueError) as error:
            context.abort(grpc.StatusCode.FAILED_PRECONDITION, f"Cannot read vision model configuration: {error}")

    def load_model(entry, context):
        nonlocal cached_model, cached_signature
        try:
            info = entry.weight_path.stat()
        except OSError:
            context.abort(grpc.StatusCode.NOT_FOUND, f"Weights file is unavailable for {entry.name}: {entry.weight_path}")
        signature = (entry.weight_path, info.st_size, info.st_mtime_ns)
        with inference_lock:
            if not context.is_active():
                return None
            if cached_signature != signature:
                try:
                    model = load_checkpoint(entry.weight_path)
                except Exception as error:
                    context.abort(grpc.StatusCode.FAILED_PRECONDITION,
                                  f"Cannot load weights for {entry.name}: {error}")
                cached_model, cached_signature = model, signature
            return cached_model

    class VisionService(rpc.AmlabMonitorVisionServicer):
        def ListModels(self, request, context):
            return pb.ListModelsReply(models=[pb.VisionModel(
                id=entry.id, algorithm_name="YOLO", model_name=entry.name,
                default_confidence=0.25, default_iou=0.70,
            ) for entry in get_models(context).values()])

        def Detect(self, requests, context):
            first = next(requests, None)
            if first is None or first.WhichOneof("input") != "options":
                context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Send detection options first.")
            options = first.options
            entry = get_models(context).get(options.model_id)
            if entry is None:
                context.abort(grpc.StatusCode.NOT_FOUND, "Unknown model ID. Refresh the vision model list.")
            if not all(math.isfinite(value) and 0 <= value <= 1
                       for value in (options.confidence, options.iou)):
                context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Confidence and IoU must be between 0 and 1.")

            last_id = 0
            model = None
            for request in requests:
                if request.WhichOneof("input") != "frame":
                    context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Start a new call to change detection options.")
                frame = request.frame
                if frame.frame_id <= last_id or not 0 < len(frame.image) <= MAX_IMAGE_BYTES:
                    context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Use increasing frame IDs and images up to 12 MiB.")
                last_id = frame.frame_id
                image = cv2.imdecode(np.frombuffer(frame.image, dtype=np.uint8),
                                     cv2.IMREAD_COLOR | cv2.IMREAD_IGNORE_ORIENTATION)
                if image is None:
                    context.abort(grpc.StatusCode.INVALID_ARGUMENT, "The image could not be decoded.")
                if model is None:
                    model = load_model(entry, context)
                    if model is None:
                        return
                try:
                    with inference_lock:
                        if not context.is_active():
                            return
                        result = model.predict(
                            image, imgsz=640, conf=options.confidence, iou=options.iou,
                            device=device, verbose=False, save=False
                        )[0]
                        coordinates = result.boxes.xyxy.cpu().tolist()
                        classes = result.boxes.cls.int().cpu().tolist()
                        scores = result.boxes.conf.cpu().tolist()
                except Exception as error:
                    print(f"Detection failed: {error}", file=sys.stderr, flush=True)
                    context.abort(grpc.StatusCode.INTERNAL, f"YOLO inference failed: {error}")
                height, width = result.orig_shape
                yield pb.DetectionResult(
                    frame_id=frame.frame_id, width=width, height=height,
                    defects=[
                        pb.DefectBox(class_id=index, class_name=result.names[index], confidence=score,
                                     x_min=box[0], y_min=box[1], x_max=box[2], y_max=box[3])
                        for box, index, score in zip(coordinates, classes, scores)
                    ],
                )

    server = grpc.server(ThreadPoolExecutor(max_workers=4), options=[
        ("grpc.max_receive_message_length", MAX_IMAGE_BYTES + 4096)
    ])
    rpc.add_AmlabMonitorVisionServicer_to_server(VisionService(), server)
    bound_port = server.add_insecure_port(f"127.0.0.1:{port}")
    server.start()
    print(f"Amlab_Monitor vision listening on 127.0.0.1:{bound_port} ({device})", flush=True)
    print(f"AMLAB_MONITOR_READY={bound_port}", flush=True)
    try:
        server.wait_for_termination()
    except KeyboardInterrupt:
        server.stop(0).wait()


if __name__ == "__main__":
    main()
