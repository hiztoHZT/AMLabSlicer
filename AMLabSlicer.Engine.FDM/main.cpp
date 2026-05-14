// ================================================================
// AMLabSlicer FDM Engine - C++ gRPC 服务端
// 监听 :50100，实现 SlicerService 协议
// ================================================================

#include <iostream>
#include <string>
#include <sstream>
#include <memory>
#include <functional>

#include <grpcpp/grpcpp.h>
#include "slicer.grpc.pb.h"

#include "slicer/FdmSlicer.h"
#include "slicer/MeshCutter.h"
#include "slicer/Infill.h"
#include "slicer/GCodeWriter.h"

using namespace amlabslicer::proto;
using ::grpc::Server;
using ::grpc::ServerBuilder;
using ::grpc::ServerContext;
using ::grpc::Status;

// Forward declaration
namespace fdm {
    std::string SliceMesh(
        const float* vertices, int vertexCount,
        const int* indices, int indexCount,
        const SliceParams& params,
        ProgressCallback progressCb);
}

// 从 params map 中安全读取
static float GetFloat(const google::protobuf::Map<std::string, std::string>& m, const std::string& key, float def)
{
    auto it = m.find(key);
    if (it == m.end()) return def;
    try { return std::stof(it->second); } catch (...) { return def; }
}
static int GetInt(const google::protobuf::Map<std::string, std::string>& m, const std::string& key, int def)
{
    auto it = m.find(key);
    if (it == m.end()) return def;
    try { return std::stoi(it->second); } catch (...) { return def; }
}
static bool GetBool(const google::protobuf::Map<std::string, std::string>& m, const std::string& key, bool def)
{
    auto it = m.find(key);
    if (it == m.end()) return def;
    return (it->second == "true" || it->second == "True" || it->second == "1");
}
static std::string GetStr(const google::protobuf::Map<std::string, std::string>& m, const std::string& key, const std::string& def)
{
    auto it = m.find(key);
    return (it != m.end()) ? it->second : def;
}

// ── 参数模板定义辅助宏 ─────────────────────────────────
static ParameterTemplate MakeParam(
    const std::string& key, const std::string& name,
    const std::string& cat, const std::string& subcat,
    int order, ValueType vt, ControlType ct,
    const std::string& defVal, double minV, double maxV, double step,
    const std::string& unit, const std::string& desc,
    bool advanced = false, const std::string& visIf = "", const std::string& enIf = "")
{
    ParameterTemplate p;
    p.set_key(key);
    p.set_display_name(name);
    p.set_category(cat);
    p.set_subcategory(subcat);
    p.set_order(order);
    p.set_value_type(vt);
    p.set_control_type(ct);
    p.set_default_value(defVal);
    p.set_min_value(minV);
    p.set_max_value(maxV);
    p.set_step(step);
    p.set_unit(unit);
    p.set_description(desc);
    p.set_is_advanced(advanced);
    if (!visIf.empty()) p.set_visible_if(visIf);
    if (!enIf.empty()) p.set_enabled_if(enIf);
    return p;
}

// ================================================================
class FdmSlicerService final : public SlicerService::Service
{
public:
    Status GetAvailableAlgorithms(ServerContext*, const Empty*, AlgorithmList* reply) override
    {
        reply->add_algorithms("FDM 三轴切片");
        return Status::OK;
    }

    Status GetAlgorithmParameters(ServerContext*, const AlgorithmRequest*, ParameterTemplateList* reply) override
    {
        // ── 质量 ──
        *reply->add_parameters() = MakeParam("layer_height", "层高", "质量", "层高设置", 0, VT_FLOAT, SLIDER, "0.2", 0.04, 0.6, 0.05, "mm", "每层的打印厚度，越小越精细但越慢");
        *reply->add_parameters() = MakeParam("initial_layer_height", "初始层高", "质量", "层高设置", 1, VT_FLOAT, NUMERIC_BOX, "0.3", 0.1, 0.6, 0.05, "mm", "第一层的厚度，稍厚有助于附着", true);
        *reply->add_parameters() = MakeParam("line_width", "线宽", "质量", "线宽设置", 2, VT_FLOAT, NUMERIC_BOX, "0.4", 0.2, 1.0, 0.05, "mm", "挤出线条的宽度", true);

        // ── 壁 ──
        *reply->add_parameters() = MakeParam("wall_line_count", "壁数", "壁", "壁设置", 0, VT_INT, NUMERIC_BOX, "3", 1, 20, 1, "", "外壁的圈数");

        // ── 顶部/底部 ──
        *reply->add_parameters() = MakeParam("top_layers", "顶层数", "顶部/底部", "顶底层数", 0, VT_INT, NUMERIC_BOX, "4", 0, 20, 1, "", "模型顶部的实心层数");
        *reply->add_parameters() = MakeParam("bottom_layers", "底层数", "顶部/底部", "顶底层数", 1, VT_INT, NUMERIC_BOX, "4", 0, 20, 1, "", "模型底部的实心层数");

        // ── 填充 ──
        *reply->add_parameters() = MakeParam("infill_sparse_density", "填充密度", "填充", "填充设置", 0, VT_FLOAT, SLIDER, "20", 0, 100, 5, "%", "内部填充的百分比，0=空心，100=实心");
        {
            auto* p = reply->add_parameters();
            *p = MakeParam("infill_pattern", "填充图案", "填充", "填充设置", 1, VT_ENUM, COMBO_BOX, "Lines", 0, 0, 0, "", "填充线条的排列方式");
            p->add_options("Lines");
            p->add_options("Grid");
            p->add_options("Triangles");
            p->add_options("Gyroid");
        }

        // ── 速度 ──
        *reply->add_parameters() = MakeParam("speed_print", "打印速度", "速度", "通用速度", 0, VT_FLOAT, SLIDER, "60", 10, 300, 10, "mm/s", "打印时喷头的移动速度");
        *reply->add_parameters() = MakeParam("speed_travel", "行进速度", "速度", "通用速度", 1, VT_FLOAT, SLIDER, "120", 30, 500, 10, "mm/s", "非打印时喷头的移动速度", true);
        *reply->add_parameters() = MakeParam("speed_layer_0", "初始层速度", "速度", "初始层", 2, VT_FLOAT, SLIDER, "20", 5, 60, 5, "mm/s", "第一层的打印速度，慢一些有助于附着", true);

        // ── 温度 ──
        *reply->add_parameters() = MakeParam("material_print_temperature", "喷头温度", "温度", "喷头", 0, VT_FLOAT, NUMERIC_BOX, "200", 150, 300, 5, "°C", "喷头加热温度");
        *reply->add_parameters() = MakeParam("material_bed_temperature", "热床温度", "温度", "热床", 1, VT_FLOAT, NUMERIC_BOX, "60", 0, 120, 5, "°C", "热床加热温度");

        // ── 回抽 ──
        *reply->add_parameters() = MakeParam("retraction_enable", "启用回抽", "回抽", "回抽设置", 0, VT_BOOL, CHECK_BOX, "true", 0, 0, 0, "", "在行进时回抽耗材以防止拉丝");
        *reply->add_parameters() = MakeParam("retraction_amount", "回抽距离", "回抽", "回抽设置", 1, VT_FLOAT, NUMERIC_BOX, "5", 0, 20, 0.5, "mm", "回抽的耗材长度", false, "retraction_enable==true");
        *reply->add_parameters() = MakeParam("retraction_speed", "回抽速度", "回抽", "回抽设置", 2, VT_FLOAT, NUMERIC_BOX, "45", 10, 100, 5, "mm/s", "回抽时的速度", true, "retraction_enable==true");

        // ── 冷却 ──
        *reply->add_parameters() = MakeParam("cool_fan_speed", "风扇速度", "冷却", "风扇", 0, VT_FLOAT, SLIDER, "100", 0, 100, 10, "%", "冷却风扇的转速百分比");

        // ── 支撑 ──
        *reply->add_parameters() = MakeParam("support_enable", "启用支撑", "支撑", "支撑设置", 0, VT_BOOL, CHECK_BOX, "false", 0, 0, 0, "", "为悬垂区域生成支撑结构");
        *reply->add_parameters() = MakeParam("support_angle", "支撑角度", "支撑", "支撑设置", 1, VT_FLOAT, SLIDER, "50", 0, 90, 5, "°", "大于此角度的悬垂才生成支撑", false, "support_enable==true");

        // ── 附着 ──
        {
            auto* p = reply->add_parameters();
            *p = MakeParam("adhesion_type", "附着类型", "附着", "附着设置", 0, VT_ENUM, COMBO_BOX, "Skirt", 0, 0, 0, "", "第一层附着方式");
            p->add_options("None");
            p->add_options("Skirt");
            p->add_options("Brim");
            p->add_options("Raft");
        }

        // ── 机器 ──
        *reply->add_parameters() = MakeParam("machine_nozzle_size", "喷嘴直径", "机器", "喷嘴", 0, VT_FLOAT, NUMERIC_BOX, "0.4", 0.1, 1.0, 0.1, "mm", "喷嘴孔径大小");
        *reply->add_parameters() = MakeParam("material_diameter", "耗材直径", "机器", "耗材", 1, VT_FLOAT, NUMERIC_BOX, "1.75", 1.0, 3.0, 0.25, "mm", "耗材丝的直径 (1.75 或 2.85)");

        return Status::OK;
    }

    Status Slice(ServerContext* context,
                 ::grpc::ServerReaderWriter<SliceServerMessage, SliceClientMessage>* stream) override
    {
        SliceClientMessage clientMsg;
        while (stream->Read(&clientMsg))
        {
            if (clientMsg.msg_case() == SliceClientMessage::kCancelRequest)
            {
                SliceServerMessage m;
                auto* r = m.mutable_result();
                r->set_success(false);
                r->set_message("用户取消切片");
                stream->Write(m);
                return Status::OK;
            }

            if (clientMsg.msg_case() == SliceClientMessage::kStartRequest)
            {
                const auto& req = clientMsg.start_request();
                const auto& pm = req.global_parameters();

                // 解析参数
                fdm::SliceParams params;
                params.layerHeight       = GetFloat(pm, "layer_height", 0.2f);
                params.initialLayerHeight = GetFloat(pm, "initial_layer_height", 0.3f);
                params.lineWidth         = GetFloat(pm, "line_width", 0.4f);
                params.wallLineCount     = GetInt(pm, "wall_line_count", 3);
                params.infillDensity     = GetFloat(pm, "infill_sparse_density", 20.0f);
                params.infillPattern     = GetStr(pm, "infill_pattern", "Lines");
                params.speedPrint        = GetFloat(pm, "speed_print", 60.0f);
                params.speedTravel       = GetFloat(pm, "speed_travel", 120.0f);
                params.speedLayer0       = GetFloat(pm, "speed_layer_0", 20.0f);
                params.nozzleTemp        = GetFloat(pm, "material_print_temperature", 200.0f);
                params.bedTemp           = GetFloat(pm, "material_bed_temperature", 60.0f);
                params.retractionEnable  = GetBool(pm, "retraction_enable", true);
                params.retractionAmount  = GetFloat(pm, "retraction_amount", 5.0f);
                params.retractionSpeed   = GetFloat(pm, "retraction_speed", 45.0f);
                params.fanSpeed          = GetFloat(pm, "cool_fan_speed", 100.0f);
                params.nozzleSize        = GetFloat(pm, "machine_nozzle_size", 0.4f);
                params.filamentDiameter  = GetFloat(pm, "material_diameter", 1.75f);

                // 合并所有 MeshObject 的顶点和索引
                std::vector<float> allVerts;
                std::vector<int>   allIndices;
                int vertOffset = 0;

                for (const auto& obj : req.objects())
                {
                    size_t vertBytes = obj.vertices().size();
                    size_t idxBytes  = obj.indices().size();
                    int nFloats = (int)(vertBytes / sizeof(float));
                    int nInts   = (int)(idxBytes / sizeof(int));

                    // 调试日志：打印每个对象的原始数据大小
                    {
                        SliceServerMessage m;
                        auto* log = m.mutable_log();
                        log->set_level(LogMessage::INFO);
                        log->set_text("  Object '" + obj.name() + "': " +
                            std::to_string(vertBytes) + " bytes vertices (" +
                            std::to_string(nFloats) + " floats = " +
                            std::to_string(nFloats / 3) + " verts), " +
                            std::to_string(idxBytes) + " bytes indices (" +
                            std::to_string(nInts) + " ints = " +
                            std::to_string(nInts / 3) + " tris)");
                        stream->Write(m);
                    }

                    if (nFloats == 0 || nInts == 0) continue;

                    int prevVertSize = (int)allVerts.size();
                    allVerts.resize(prevVertSize + nFloats);
                    std::memcpy(allVerts.data() + prevVertSize,
                                obj.vertices().data(), vertBytes);

                    int prevIdxSize = (int)allIndices.size();
                    allIndices.resize(prevIdxSize + nInts);
                    const int* srcIdx = reinterpret_cast<const int*>(obj.indices().data());
                    for (int i = 0; i < nInts; ++i)
                        allIndices[prevIdxSize + i] = srcIdx[i] + vertOffset;

                    vertOffset += nFloats / 3;
                }

                int totalVerts = (int)allVerts.size() / 3;
                int totalTris  = (int)allIndices.size() / 3;

                // 调试：打印前几个顶点坐标
                {
                    SliceServerMessage m;
                    auto* log = m.mutable_log();
                    log->set_level(LogMessage::INFO);
                    std::string dbg = "Mesh summary: " +
                        std::to_string(totalVerts) + " verts, " +
                        std::to_string(totalTris) + " tris";

                    if (totalVerts > 0)
                    {
                        dbg += " | First 3 verts: ";
                        for (int i = 0; i < std::min(3, totalVerts); ++i)
                        {
                            dbg += "(" + std::to_string(allVerts[i*3]) + ", "
                                       + std::to_string(allVerts[i*3+1]) + ", "
                                       + std::to_string(allVerts[i*3+2]) + ") ";
                        }
                    }

                    if (totalTris > 0)
                    {
                        dbg += " | First tri indices: " +
                            std::to_string(allIndices[0]) + ", " +
                            std::to_string(allIndices[1]) + ", " +
                            std::to_string(allIndices[2]);
                    }

                    log->set_text(dbg);
                    stream->Write(m);
                }

                if (totalVerts == 0 || totalTris == 0)
                {
                    SliceServerMessage errMsg;
                    auto* r = errMsg.mutable_result();
                    r->set_success(false);
                    r->set_message("No mesh data received (verts=" +
                        std::to_string(totalVerts) + ", tris=" +
                        std::to_string(totalTris) + ")");
                    stream->Write(errMsg);
                    return Status::OK;
                }

                // 进度回调 → 通过 gRPC 流发回
                auto progressCb = [&stream](float progress, const std::string& stage) {
                    SliceServerMessage m;
                    auto* p = m.mutable_progress();
                    p->set_progress(progress);
                    p->set_current_stage(stage);
                    stream->Write(m);
                };

                // 执行切片
                std::string gcode = fdm::SliceMesh(
                    allVerts.data(), (int)allVerts.size() / 3,
                    allIndices.data(), (int)allIndices.size(),
                    params, progressCb);

                // 发送结果
                SliceServerMessage resultMsg;
                auto* result = resultMsg.mutable_result();
                result->set_success(true);
                result->set_message("切片完成");
                result->set_gcode(gcode);
                result->set_filament_used_mm(gcode.size() > 0 ? 1000.0 : 0);  // 简化估算
                stream->Write(resultMsg);

                return Status::OK;
            }
        }
        return Status::OK;
    }
};

// ================================================================
int main(int argc, char** argv)
{
    std::string addr = "0.0.0.0:50100";

    FdmSlicerService service;
    ServerBuilder builder;
    builder.SetMaxReceiveMessageSize(-1); // -1 means unlimited
    builder.SetMaxSendMessageSize(-1);
    builder.AddListeningPort(addr, ::grpc::InsecureServerCredentials());
    builder.RegisterService(&service);

    auto server = builder.BuildAndStart();
    std::cout << "========================================" << std::endl;
    std::cout << "  AMLabSlicer FDM Engine (C++)" << std::endl;
    std::cout << "  gRPC 监听: " << addr << std::endl;
    std::cout << "========================================" << std::endl;

    server->Wait();
    return 0;
}
