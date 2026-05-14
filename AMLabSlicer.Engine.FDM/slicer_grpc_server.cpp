// Server-only gRPC implementation
// Based on auto-generated code, with all Client Stub code removed
// to avoid protobuf v33 / gRPC v1.76 API incompatibility

#include "slicer.pb.h"
#include "slicer.grpc.pb.h"

#include <grpcpp/support/method_handler.h>
#include <grpcpp/impl/rpc_service_method.h>
#include <grpcpp/support/sync_stream.h>
#include <grpcpp/ports_def.inc>

namespace amlabslicer {
namespace proto {

static const char* SlicerService_method_names[] = {
    "/amlabslicer.proto.SlicerService/GetAvailableAlgorithms",
    "/amlabslicer.proto.SlicerService/GetAlgorithmParameters",
    "/amlabslicer.proto.SlicerService/Slice",
};

SlicerService::Service::Service() {
    AddMethod(new ::grpc::internal::RpcServiceMethod(
        SlicerService_method_names[0],
        ::grpc::internal::RpcMethod::NORMAL_RPC,
        new ::grpc::internal::RpcMethodHandler< SlicerService::Service, ::amlabslicer::proto::Empty, ::amlabslicer::proto::AlgorithmList, ::grpc::protobuf::MessageLite, ::grpc::protobuf::MessageLite>(
            [](SlicerService::Service* service, ::grpc::ServerContext* ctx,
               const ::amlabslicer::proto::Empty* req, ::amlabslicer::proto::AlgorithmList* resp) {
                return service->GetAvailableAlgorithms(ctx, req, resp);
            }, this)));
    AddMethod(new ::grpc::internal::RpcServiceMethod(
        SlicerService_method_names[1],
        ::grpc::internal::RpcMethod::NORMAL_RPC,
        new ::grpc::internal::RpcMethodHandler< SlicerService::Service, ::amlabslicer::proto::AlgorithmRequest, ::amlabslicer::proto::ParameterTemplateList, ::grpc::protobuf::MessageLite, ::grpc::protobuf::MessageLite>(
            [](SlicerService::Service* service, ::grpc::ServerContext* ctx,
               const ::amlabslicer::proto::AlgorithmRequest* req, ::amlabslicer::proto::ParameterTemplateList* resp) {
                return service->GetAlgorithmParameters(ctx, req, resp);
            }, this)));
    AddMethod(new ::grpc::internal::RpcServiceMethod(
        SlicerService_method_names[2],
        ::grpc::internal::RpcMethod::BIDI_STREAMING,
        new ::grpc::internal::BidiStreamingHandler< SlicerService::Service, ::amlabslicer::proto::SliceClientMessage, ::amlabslicer::proto::SliceServerMessage>(
            [](SlicerService::Service* service, ::grpc::ServerContext* ctx,
               ::grpc::ServerReaderWriter<::amlabslicer::proto::SliceServerMessage,
               ::amlabslicer::proto::SliceClientMessage>* stream) {
                return service->Slice(ctx, stream);
            }, this)));
}

SlicerService::Service::~Service() {}

::grpc::Status SlicerService::Service::GetAvailableAlgorithms(::grpc::ServerContext*, const ::amlabslicer::proto::Empty*, ::amlabslicer::proto::AlgorithmList*) {
    return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status SlicerService::Service::GetAlgorithmParameters(::grpc::ServerContext*, const ::amlabslicer::proto::AlgorithmRequest*, ::amlabslicer::proto::ParameterTemplateList*) {
    return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

::grpc::Status SlicerService::Service::Slice(::grpc::ServerContext*, ::grpc::ServerReaderWriter< ::amlabslicer::proto::SliceServerMessage, ::amlabslicer::proto::SliceClientMessage>*) {
    return ::grpc::Status(::grpc::StatusCode::UNIMPLEMENTED, "");
}

}  // namespace proto
}  // namespace amlabslicer

#include <grpcpp/ports_undef.inc>
