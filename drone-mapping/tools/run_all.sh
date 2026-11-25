#!/usr/bin/env bash
# 경사촬영 사진 → 3D 매핑 한 번에 (1125 시험으로 정한 차례)
#   COLMAP 4.2.1: 특징점(GPU) → GPS 이웃 맞추기(GPU) → RTK 위치 카메라 풀기 → 왜곡 펴기
#   OpenMVS 2.4.0: 촘촘한 점구름(CPU — CUDA 판은 RTX 50 커널 없음) → 3D 면(CPU)
#   COLMAP: 질감 (OpenMVS 질감은 이 판에서 질감 좌표가 그림과 어긋남 · COLMAP 은 99.9 % 맞음 · 38초)
# 사용: bash run_all.sh 작업폴더 사진폴더 [품질: fast|normal|fine]
#   질감용 사진 = 원본의 1/4 · 1/2 · 원본 · 점구름은 그 절반 (normal = 1125 80장 시험 값: 질감 2640 · 점구름 1320)
set -u
C="/d/매핑시험/engines/colmap/bin/colmap.exe"
M="/d/매핑시험/engines/openmvs"
export PATH="/d/매핑시험/engines/colmap/bin:/d/매핑시험/engines/colmap/lib:$M:$PATH"
WS="$1"; IMG="$2"; Q="${3:-normal}"
case "$Q" in fast) RES=1320; FEAT=2000;; fine) RES=5280; FEAT=4000;; *) RES=2640; FEAT=3200;; esac
mkdir -p "$WS/sparse" "$WS/sparse_txt" "$WS/dense"
LOG="$WS/시간.txt"
stamp() { echo "$(date +%H:%M:%S) $*" | tee -a "$LOG"; }
step() { local name="$1"; shift; local t=$(date +%s); "$@"; local rc=$?; stamp "$name $(( $(date +%s) - t ))초 · 종료코드 $rc"; return $rc; }
T0=$(date +%s)
stamp "시작 · 사진 $(ls "$IMG" | grep -i -c '\.jpg$')장 · 품질 $Q (해상도 $RES)"
step 특징점 "$C" feature_extractor --database_path "$WS/db.db" --image_path "$IMG" \
  --ImageReader.single_camera 1 --ImageReader.camera_model OPENCV --ImageReader.camera_params "3725.15,3725.15,2640,1978,0,0,0,0" \
  --FeatureExtraction.max_image_size "$FEAT" --FeatureExtraction.use_gpu 1 > "$WS/1_features.log" 2>&1 || exit 1
step 맞추기 "$C" spatial_matcher --database_path "$WS/db.db" --SpatialMatching.max_num_neighbors 40 \
  --SpatialMatching.max_distance 300 --FeatureMatching.use_gpu 1 > "$WS/2_match.log" 2>&1 || exit 1
step 카메라풀기 "$C" pose_prior_mapper --database_path "$WS/db.db" --image_path "$IMG" --output_path "$WS/sparse" \
  --overwrite_priors_covariance 1 --prior_position_std_x 0.05 --prior_position_std_y 0.05 --prior_position_std_z 0.1 \
  --use_robust_loss_on_prior_position 1 > "$WS/3_mapper.log" 2>&1 || exit 1
"$C" model_analyzer --path "$WS/sparse/0" > "$WS/sparse/0/analyze.txt" 2>&1
stamp "모델: $(grep -E 'Registered images|Points|Mean reprojection error' "$WS/sparse/0/analyze.txt" | sed 's/.*\] //' | tr '\n' ' ')"
"$C" model_converter --input_path "$WS/sparse/0" --output_path "$WS/sparse_txt" --output_type TXT > /dev/null 2>&1
step 왜곡펴기 "$C" image_undistorter --image_path "$IMG" --input_path "$WS/sparse/0" --output_path "$WS/dense" \
  --output_type COLMAP --max_image_size "$RES" > "$WS/4_undistort.log" 2>&1 || exit 1
cd "$WS/dense"
step OpenMVS넘기기 "$M/InterfaceCOLMAP.exe" -i . -o scene.mvs --image-folder images -w . > /dev/null 2>&1
step 점구름 "$M/DensifyPointCloud.exe" scene.mvs -o scene_cpu.mvs --resolution-level 1 --max-resolution "$RES" --cuda-device -2 -w . > /dev/null 2>&1
step 3D면 "$M/ReconstructMesh.exe" scene_cpu.mvs -o scene_cpu_mesh.mvs --cuda-device -2 -w . > /dev/null 2>&1
cd "$WS"
mkdir -p tex_colmap
step 질감 "$C" mesh_texturer --workspace_path dense --input_path dense/scene_cpu_mesh.ply --output_path tex_colmap > "$WS/7_texturer.log" 2>&1
stamp "끝 · 전체 $(( $(date +%s) - T0 ))초"
