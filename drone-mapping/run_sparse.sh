#!/usr/bin/env bash
# 사진 맞추기 시험 (COLMAP 4.2.1 CUDA) — 특징점 → GPS 이웃 맞추기 → RTK 위치를 쓰는 카메라 풀기
# 사용: bash run_sparse.sh 작업폴더 사진폴더
set -u
C="/d/매핑시험/engines/colmap/bin/colmap.exe"
export PATH="/d/매핑시험/engines/colmap/bin:/d/매핑시험/engines/colmap/lib:$PATH"
WS="$1"; IMG="$2"
mkdir -p "$WS/sparse"
LOG="$WS/시간.txt"
stamp() { echo "$(date +%H:%M:%S) $*" | tee -a "$LOG"; }
T0=$(date +%s)
stamp "시작 · 사진 $(ls "$IMG" | wc -l)장"
t=$(date +%s)
"$C" feature_extractor --database_path "$WS/db.db" --image_path "$IMG" \
  --ImageReader.single_camera 1 --ImageReader.camera_model OPENCV \
  --ImageReader.camera_params "3725.15,3725.15,2640,1978,0,0,0,0" \
  --FeatureExtraction.max_image_size 3200 --FeatureExtraction.use_gpu 1 > "$WS/1_features.log" 2>&1
stamp "특징점 $(( $(date +%s) - t ))초 · 종료코드 $?"
t=$(date +%s)
"$C" spatial_matcher --database_path "$WS/db.db" --SpatialMatching.max_num_neighbors 40 \
  --SpatialMatching.max_distance 300 --FeatureMatching.use_gpu 1 > "$WS/2_match.log" 2>&1
stamp "맞추기 $(( $(date +%s) - t ))초 · 종료코드 $?"
t=$(date +%s)
"$C" pose_prior_mapper --database_path "$WS/db.db" --image_path "$IMG" --output_path "$WS/sparse" \
  --overwrite_priors_covariance 1 --prior_position_std_x 0.05 --prior_position_std_y 0.05 --prior_position_std_z 0.1 \
  --use_robust_loss_on_prior_position 1 > "$WS/3_mapper.log" 2>&1
stamp "카메라 풀기 $(( $(date +%s) - t ))초 · 종료코드 $?"
for m in "$WS"/sparse/*/; do
  "$C" model_analyzer --path "$m" > "$m/analyze.txt" 2>&1
  stamp "모델 $m: $(grep -E 'Registered images|Points|Mean reprojection error' "$m/analyze.txt" | sed 's/.*\] //' | tr '\n' ' ')"
done
stamp "끝 · 전체 $(( $(date +%s) - T0 ))초"
