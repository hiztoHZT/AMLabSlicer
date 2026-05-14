cmake_minimum_required(VERSION 3.20)
project(AMLabSlicer_Engine_FDM LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(protobuf CONFIG REQUIRED)
find_package(gRPC CONFIG REQUIRED)

# Proto 文件 → 只生成 protobuf 消息
set(PROTO_FILE "${CMAKE_SOURCE_DIR}/../Protos/slicer.proto")
set(PROTO_PATH "${CMAKE_SOURCE_DIR}/../Protos")
set(PROTO_OUT "${CMAKE_BINARY_DIR}/generated")
file(MAKE_DIRECTORY ${PROTO_OUT})

add_custom_command(
    OUTPUT "${PROTO_OUT}/slicer.pb.cc" "${PROTO_OUT}/slicer.pb.h"
    COMMAND $<TARGET_FILE:protobuf::protoc>
        --proto_path=${PROTO_PATH}
        --cpp_out=${PROTO_OUT}
        ${PROTO_FILE}
    DEPENDS ${PROTO_FILE}
    COMMENT "Generating protobuf messages from slicer.proto"
)

# gRPC .grpc.pb.h 也自动生成（但 .grpc.pb.cc 我们用裁剪后的 slicer_grpc_server.cpp 代替）
add_custom_command(
    OUTPUT "${PROTO_OUT}/slicer.grpc.pb.h"
    COMMAND $<TARGET_FILE:protobuf::protoc>
        --proto_path=${PROTO_PATH}
        --grpc_out=${PROTO_OUT}
        --plugin=protoc-gen-grpc=$<TARGET_FILE:gRPC::grpc_cpp_plugin>
        ${PROTO_FILE}
    DEPENDS ${PROTO_FILE}
    COMMENT "Generating gRPC header from slicer.proto"
)

add_executable(fdm_engine
    main.cpp
    slicer_grpc_server.cpp      # 手动裁剪的 server-only 版本
    slicer/FdmSlicer.cpp
    slicer/MeshCutter.cpp
    slicer/Infill.cpp
    slicer/GCodeWriter.cpp 
    ${PROTO_OUT}/slicer.pb.cc
    ${PROTO_OUT}/slicer.grpc.pb.h  # header dependency
)

target_include_directories(fdm_engine PRIVATE
    ${CMAKE_SOURCE_DIR}
    ${PROTO_OUT}
)

target_link_libraries(fdm_engine PRIVATE
    gRPC::grpc++
    protobuf::libprotobuf
)

# MSVC: UTF-8 支持
if(MSVC)
    target_compile_options(fdm_engine PRIVATE /utf-8)
endif()
 