cmake_minimum_required(VERSION 3.20)
project(AMLabKinematics LANGUAGES CXX)
add_library(amlab_kinematics SHARED kinematics.cpp)
target_compile_features(amlab_kinematics PRIVATE cxx_std_17)
