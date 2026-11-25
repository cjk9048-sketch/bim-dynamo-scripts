#!/usr/bin/env bash
# 촘촘한 점구름 → 3D 면 → 질감 (COLMAP 왜곡 펴기 + OpenMVS 2.4 CUDA)
# 사용: bash run_dense.sh 작업폴더 사진폴더 [최대 해상도=2640]
set -u
C="/d/매핑시험/engines/colmap/bin/colmap.exe"
M="/d/매핑시험/engines/openmvs"
export PATH="/d/매핑시험/engines/colmap/bin:/d/매핑시험/engines/colmap/lib:$M:$PATH"
WS="$1"; IMG="$2"; RES="${3:-2640}"
D="$WS/dense"
LOG="$WS/시간.txt"
stamp() { echo "$(date +%H:%M:%S) $*" | tee -a "$LOG"; }
T0=$(date +%s)
mkdir -p "$D"
t=$(date +%s)
"$C" image_undistorter --image_path "$IMG" --input_path "$WS/sparse/0" --output_path "$D" --output_type COLMAP \
  --max_image_size "$RES" > "$WS/4_undistort.log" 2>&1
stamp "왜곡 펴기 $(( $(date +%s) - t ))초 · 종료코드 $?"
cd "$D"
t=$(date +%s)
"$M/InterfaceCOLMAP.exe" -i . -o scene.mvs --image-folder images -w . > /dev/null 2>&1
stamp "OpenMVS 넘기기 $(( $(date +%s) - t ))초 · 종료코드 $?"
t=$(date +%s)
"$M/DensifyPointCloud.exe" scene.mvs --resolution-level 0 --max-resolution "$RES" --cuda-device -1 -w . > /dev/null 2>&1
stamp "촘촘한 점구름 $(( $(date +%s) - t ))초 · 종료코드 $? · $(ls -la scene_dense.ply 2>/dev/null | awk '{print $5/1e6 " MB"}')"
t=$(date +%s)
"$M/ReconstructMesh.exe" scene_dense.mvs -w . > /dev/null 2>&1
stamp "3D 면 $(( $(date +%s) - t ))초 · 종료코드 $? · $(ls -la scene_dense_mesh.ply 2>/dev/null | awk '{print $5/1e6 " MB"}')"
t=$(date +%s)
"$M/TextureMesh.exe" scene_dense_mesh.mvs --export-type obj -w . > /dev/null 2>&1
stamp "질감 $(( $(date +%s) - t ))초 · 종료코드 $? · $(ls scene_dense_mesh_texture* 2>/dev/null | head -3 | tr '\n' ' ')"
stamp "촘촘 단계 끝 · $(( $(date +%s) - T0 ))초"
