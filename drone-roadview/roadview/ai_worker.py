"""AI 부대공 인식 작업 — AI 환경(%LOCALAPPDATA%\\DH.Roadview\\ai\\venv)의 python 으로만 돈다 (ai_budae.py 가 부름).

    fetch  : 모델 내려받기 (RV_AI_MODELS\\<모델 이름> 에 파일 그대로 — HF 캐시는 링크를 만들다 D: 에서 권한 오류 · 1126 시험)
    run    : 정사 · 3D 면 · (360 사진) → 부대공 후보 (result_raw.json · 3D 화면 좌표 = 도면좌표 − origin)

차례 (run)
  1. 정사영상(결과물/정사영상.tif + tfw) → 작업 칸(기본 5 cm)으로 줄임 · 3D 면(data/tiles/*.bin) → 같은 칸 DSM (가장 높은 면)
  2. 정사를 겹치게 타일로 → Grounding DINO(글 → 상자) → SAM(상자 → 영역) → CLIP 으로 영역 종류 다시 확인 → 종류별 점수 칸
  3. 영역 다각형 (보도블록 · 아스팔트 · 콘크리트 · 비포장)
  4. 연석 = 포장 영역 경계를 가로지르는 DSM 단면에서 6~35 cm 턱 → 선 (턱 자리로 옮김 · 위 모서리 높이)
  5. 석축 = DSM 급경사 띠 (56° 넘음 · 높이 ≥ 0.8 m · 길쭉) → 위 · 아래 선 + 높이 (건물 · 나무 · 차 영역은 뺌)
  6. 360 근거 (360 프로젝트가 있으면) = 후보마다 잘 보이는 사진을 골라 평면으로 잘라 CLIP 으로 재질 · 상태
"""
import argparse
import base64
import glob
import io
import json
import math
import os
import re
import sys
import time

T0 = time.time()


def say(pct, label, left=None):
    if left is None:                                  # 남은 초 = 지난 시간 × 남은 몫 (10 % 넘은 뒤)
        left = int((time.time() - T0) * (100 - pct) / pct) if 10 <= pct < 100 else -1
    print('@진행 %.1f %d %s' % (pct, left, label), flush=True)


def log(msg):
    print('%5.0f초 %s' % (time.time() - T0, msg), flush=True)


def model_dir(mid):
    """모델 폴더 (RV_AI_MODELS\\IDEA-Research__grounding-dino-base …) · 없으면 HF 이름 그대로 (캐시)."""
    d = os.path.join(os.environ.get('RV_AI_MODELS', ''), mid.replace('/', '__'))
    return d if os.environ.get('RV_AI_MODELS') and os.path.exists(os.path.join(d, 'config.json')) else mid


def fetch(models):
    from huggingface_hub import snapshot_download
    root = os.environ['RV_AI_MODELS']
    keys = list(models)
    for i, k in enumerate(keys):
        say(100.0 * i / len(keys), '모델 내려받기 · %s' % models[k])
        d = os.path.join(root, models[k].replace('/', '__'))
        snapshot_download(models[k], local_dir=d, allow_patterns=['*.json', '*.safetensors', '*.txt'])
        if not glob.glob(os.path.join(d, '*.safetensors')):          # safetensors 없는 모델만 .bin
            snapshot_download(models[k], local_dir=d, allow_patterns=['pytorch_model.bin'])
    say(100, '모델 준비 끝')


# ---------------------------------------------------------------- 종류
# Grounding DINO 글 (영어 · 위에서 본 정사) → 우리 종류. roof · veg · car · water = 부대공 아님 (연석 · 석축 거짓 걸러내기에만)
DET_PROMPTS = [('paving blocks', 'sidewalk'), ('sidewalk', 'sidewalk'), ('asphalt road', 'asphalt'), ('concrete pavement', 'concrete'),
               ('dirt road', 'unpaved'), ('retaining wall', 'wall'), ('stone wall', 'wall'), ('curb', 'curb'),
               ('parking lot', 'pave'), ('farm road', 'pave'), ('building', 'roof'), ('greenhouse', 'roof'), ('tree', 'veg'), ('grass', 'veg'),
               ('farm field', 'field'), ('car', 'car')]
ROAD_PROMPTS = [('road', 'pave'), ('path', 'pave')]   # 길만 따로 찾는 두 번째 글 (좁은 농로 · 골목)
AREA = ['sidewalk', 'asphalt', 'concrete', 'unpaved']
OTHER = ['wall', 'veg', 'field', 'roof', 'car', 'water']
KEYS = AREA + OTHER                                   # 점수 칸 순서
# CLIP 으로 영역 종류 다시 확인 (위에서 본 그림)
CLIP_TOP = {
    'sidewalk': ['an aerial photo of a sidewalk paved with interlocking paving blocks', 'top view of a brick paver walkway'],
    'asphalt': ['an aerial photo of an asphalt road', 'top view of dark asphalt pavement'],
    'concrete': ['an aerial photo of a concrete pavement', 'top view of a gray cement concrete road'],
    'unpaved': ['an aerial photo of a dirt road', 'top view of a gravel parking area'],
    'wall': ['an aerial photo of a stone retaining wall', 'top view of a masonry retaining wall'],
    'veg': ['an aerial photo of trees', 'top view of grass and bushes'],
    'field': ['an aerial photo of a plowed farm field', 'top view of an agricultural field with crop rows', 'an aerial photo of a snow covered farm field'],
    'roof': ['an aerial photo of a building roof', 'top view of a house roof', 'an aerial photo of a plastic greenhouse'],
    'car': ['an aerial photo of a car', 'top view of parked vehicles'],
    'water': ['an aerial photo of water'],
}
# 360 평면 조각 (옆에서 비스듬히 본 그림) → 재질 · 상태
CLIP_SIDE_MAT = {
    'sidewalk': ['a photo of a sidewalk with interlocking paving blocks'],
    'asphalt': ['a photo of an asphalt road surface'],
    'concrete': ['a photo of a concrete road surface'],
    'unpaved': ['a photo of a dirt road with soil and gravel'],
    'field': ['a photo of a plowed farm field'],
    'veg': ['a photo of grass, bushes and trees'],
}
MAT_NAME = {'sidewalk': '보도블록', 'asphalt': '아스팔트', 'concrete': '콘크리트', 'unpaved': '흙 · 자갈', 'field': '밭', 'veg': '풀 · 나무'}
MIN_CONF = 0.3                                        # 이보다 낮은 후보는 버림 (360 이 아니라고 본 것 등)
NEAR_M = 15.0                                         # 석축은 포장 영역에서 이 거리 안만 (관로는 길 밑 · 밭 한가운데 둑은 뺌)
MAT_EN = {'sidewalk': 'paving block sidewalk', 'asphalt': 'asphalt road', 'concrete': 'concrete pavement', 'unpaved': 'dirt road'}


def _state_table(what):
    return {'양호': ['a close-up photo of a %s in good condition' % what],
            '균열': ['a close-up photo of a cracked %s' % what],
            '파손': ['a close-up photo of a broken %s with potholes and patches' % what]}
CLIP_CURB = {'concrete': ['a photo of a concrete curb along a road'], 'granite': ['a photo of a granite stone curb along a road'],
             'none': ['a photo of a flat road with no curb']}
CURB_MAT = {'concrete': '콘크리트 연석', 'granite': '화강석 연석', 'none': '연석 안 보임'}
CLIP_WALL = {'stone': ['a photo of a stone masonry retaining wall'], 'concrete': ['a photo of a concrete retaining wall'],
             'block': ['a photo of a concrete block retaining wall'], 'slope': ['a photo of a grassy slope with no wall']}
WALL_MAT = {'stone': '석축 (돌쌓기)', 'concrete': '콘크리트 옹벽', 'block': '블록 옹벽', 'slope': '벽 안 보임 (비탈)'}


# ---------------------------------------------------------------- 읽기
def read_ortho(project, cell):
    """정사 → (RGB uint8, 유효 bool, x0 왼쪽 끝, y1 위 끝, 원래 칸, 원본 RGBA 그림)."""
    import numpy as np
    from PIL import Image
    Image.MAX_IMAGE_PIXELS = None
    d = os.path.join(project, '결과물')
    im = Image.open(os.path.join(d, '정사영상.tif'))
    w = [float(x) for x in open(os.path.join(d, '정사영상.tfw')).read().split()]
    c0, xc, yc = w[0], w[4], w[5]
    x0, y1 = xc - c0 / 2, yc + c0 / 2
    W = max(1, int(round(im.size[0] * c0 / cell))); H = max(1, int(round(im.size[1] * c0 / cell)))
    rgba = im.convert('RGBA')
    sm = np.asarray(rgba.resize((W, H), Image.BILINEAR))
    return sm[..., :3].copy(), sm[..., 3] > 127, x0, y1, c0, rgba


def read_grid(path, x0, y1, cell, shape):
    """높이 GeoTIFF (+ tfw) → 작업 칸에 가장 가까운 값 (빈칸 NaN) · (칸, 원래 칸 크기) · 없으면 None."""
    import numpy as np
    from PIL import Image
    if not os.path.exists(path) or not os.path.exists(path[:-4] + '.tfw'):
        return None
    Image.MAX_IMAGE_PIXELS = None
    A = np.asarray(Image.open(path), np.float32)
    w = [float(x) for x in open(path[:-4] + '.tfw').read().split()]
    gc, gxc, gyc = w[0], w[4], w[5]
    H, W = shape
    xs = x0 + (np.arange(W) + 0.5) * cell; ys = y1 - (np.arange(H) + 0.5) * cell
    cc = np.round((xs - gxc) / gc).astype(int); rr = np.round((gyc - ys) / gc).astype(int)
    okc = (cc >= 0) & (cc < A.shape[1]); okr = (rr >= 0) & (rr < A.shape[0])
    Z = np.full((H, W), np.nan, np.float32)
    Z[np.ix_(okr, okc)] = A[np.ix_(rr[okr], cc[okc])]
    Z[Z <= -9998] = np.nan
    return Z, gc


def mesh_dsm(project, origin, x0, y1, cell, shape):
    """3D 면 조각(data/tiles/*.bin · 화면 좌표) → 작업 칸 DSM (화면 z · 칸마다 가장 높은 면) · 없으면 data/지면.tif."""
    import numpy as np
    H, W = shape
    dsm = np.full(H * W, -np.inf, np.float32)
    mj = os.path.join(project, 'data', 'mesh.json')
    n_tri = 0
    if os.path.exists(mj):
        meta = json.load(open(mj, encoding='utf-8'))
        o = meta.get('origin') or origin
        dz = np.array([o[0] - origin[0], o[1] - origin[1], o[2] - origin[2]], np.float32)   # 면 원점이 다르면 맞춤
        gx0, gy1 = x0 - origin[0], y1 - origin[1]
        for t in meta.get('tiles', []):
            nv = int(t['verts'])
            P = np.fromfile(os.path.join(project, 'data', t['bin']), np.float32, nv * 3).reshape(-1, 3, 3) + dz
            n_tri += len(P)
            L = np.max(np.linalg.norm(P - np.roll(P, 1, 1), axis=2), 1)
            ns = np.clip(np.ceil(L / (0.5 * cell)), 1, 40).astype(int)
            for n in np.unique(ns):
                T = P[ns == n]
                ij = np.array([(i, j) for i in range(n + 1) for j in range(n + 1 - i)], np.float32) / n
                b = np.c_[ij, 1 - ij.sum(1)]                                     # 무게 (k, 3)
                for s in range(0, len(T), 200000):
                    pts = np.einsum('kv,tvd->tkd', b, T[s:s + 200000]).reshape(-1, 3)
                    c = ((pts[:, 0] - gx0) / cell).astype(np.int64); r = ((gy1 - pts[:, 1]) / cell).astype(np.int64)
                    ok = (c >= 0) & (c < W) & (r >= 0) & (r < H)
                    np.maximum.at(dsm, r[ok] * W + c[ok], pts[ok, 2])
    dsm = dsm.reshape(H, W)
    dsm[~np.isfinite(dsm)] = np.nan
    src = '3D 면 %d 세모' % n_tri
    if n_tri == 0:                                   # 3D 면 없음 → 지면 (0.25~0.5 m · 연석은 못 찾음)
        g = read_grid(os.path.join(project, 'data', '지면.tif'), x0, y1, cell, shape)
        if g is not None:
            dsm = g[0] - origin[2]
            src = '지면.tif %.2f m (연석 턱은 못 봄)' % g[1]
    # 작은 빈칸 메움 (3번 · 이웃 평균)
    filled = np.isfinite(dsm)
    for _ in range(3):
        z = np.nan_to_num(dsm); acc = np.zeros_like(dsm); cnt = np.zeros_like(dsm)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                m = np.roll(np.roll(filled, dy, 0), dx, 1)
                acc += np.roll(np.roll(z, dy, 0), dx, 1) * m; cnt += m
        new = ~filled & (cnt > 2)
        if not new.any():
            break
        dsm[new] = acc[new] / cnt[new]; filled |= new
    return dsm, src


# ---------------------------------------------------------------- 모델
class Models:
    def __init__(self, ids):
        import torch
        from transformers import (AutoModelForZeroShotObjectDetection, AutoProcessor, CLIPModel, CLIPProcessor, SamModel,
                                  SamProcessor)
        self.torch = torch
        self.dev = 'cuda' if torch.cuda.is_available() else 'cpu'
        det, sam, clip = model_dir(ids['det']), model_dir(ids['sam']), model_dir(ids['clip'])
        self.det_p = AutoProcessor.from_pretrained(det)
        self.det = AutoModelForZeroShotObjectDetection.from_pretrained(det).to(self.dev).eval()
        self.sam_p = SamProcessor.from_pretrained(sam)
        self.sam = SamModel.from_pretrained(sam).to(self.dev).eval()
        self.clip_p = CLIPProcessor.from_pretrained(clip)
        self.clip = CLIPModel.from_pretrained(clip).to(self.dev).eval()
        self._txt = {}

    def detect(self, img, prompts, box_thr=0.22, text_thr=0.2):
        """그림(PIL) → [(상자 x0 y0 x1 y1, 점수, 글)]."""
        torch = self.torch
        text = ' . '.join(p for p, _ in prompts) + ' .'
        inp = self.det_p(images=img, text=text, return_tensors='pt').to(self.dev)
        with torch.no_grad():
            out = self.det(**inp)
        kw = dict(text_threshold=text_thr, target_sizes=[img.size[::-1]])
        try:
            r = self.det_p.post_process_grounded_object_detection(out, inp.input_ids, threshold=box_thr, **kw)[0]
        except TypeError:                                 # 옛 transformers (box_threshold)
            r = self.det_p.post_process_grounded_object_detection(out, inp.input_ids, box_threshold=box_thr, **kw)[0]
        labels = r.get('text_labels', r.get('labels'))
        return [(b.tolist(), float(s), str(l)) for b, s, l in zip(r['boxes'], r['scores'], labels)]

    def segment(self, img, boxes):
        """상자들 → 영역 (n, H, W) bool · SAM 점수."""
        torch = self.torch
        import numpy as np
        if not boxes:
            return np.zeros((0, img.size[1], img.size[0]), bool), []
        inp = self.sam_p(img, input_boxes=[boxes], return_tensors='pt').to(self.dev)
        with torch.no_grad():
            out = self.sam(**inp, multimask_output=False)
        m = self.sam_p.image_processor.post_process_masks(out.pred_masks.cpu(), inp['original_sizes'].cpu(),
                                                          inp['reshaped_input_sizes'].cpu())[0]
        return m[:, 0].numpy().astype(bool), out.iou_scores[0, :, 0].float().cpu().tolist()

    def segment_points(self, img, pts, batch=48):
        """점마다 SAM 영역 (점 하나 → 세 후보 중 점수 가장 높은 것) → [(영역 bool, 점수)]. 그림 특징은 한 번만."""
        torch = self.torch
        inp = self.sam_p(img, return_tensors='pt').to(self.dev)
        with torch.no_grad():
            emb = self.sam.get_image_embeddings(inp['pixel_values'])
        out = []
        for s in range(0, len(pts), batch):
            pi = self.sam_p(img, input_points=[[[list(map(float, p))] for p in pts[s:s + batch]]], return_tensors='pt').to(self.dev)
            with torch.no_grad():
                o = self.sam(image_embeddings=emb, input_points=pi['input_points'], multimask_output=True)
            m = self.sam_p.image_processor.post_process_masks(o.pred_masks.cpu(), pi['original_sizes'].cpu(),
                                                              pi['reshaped_input_sizes'].cpu())[0]           # (n, 3, H, W)
            sc = o.iou_scores[0].float().cpu()                                                                    # (n, 3)
            k = sc.argmax(1)
            for i in range(len(k)):
                out.append((m[i, k[i]].numpy().astype(bool), float(sc[i, k[i]])))
        return out

    def text_feats(self, table):
        """{종류: [글…]} → (열쇠 목록, 정규화된 글 특징 평균 (k, d))."""
        key = json.dumps(table, sort_keys=True)
        if key not in self._txt:
            torch = self.torch
            ks = list(table)
            feats = []
            for k in ks:
                inp = self.clip_p(text=table[k], return_tensors='pt', padding=True).to(self.dev)
                with torch.no_grad():
                    f = self.clip.get_text_features(**inp)
                f = f / f.norm(dim=-1, keepdim=True)
                f = f.mean(0)
                feats.append(f / f.norm())
            self._txt[key] = (ks, torch.stack(feats))
        return self._txt[key]

    def classify(self, imgs, table):
        """그림들 → [{종류: 확률}] (CLIP 영점 분류 · 그림은 네모로 채워 넣음)."""
        torch = self.torch
        if not imgs:
            return []
        ks, T = self.text_feats(table)
        res = []
        for s in range(0, len(imgs), 32):
            inp = self.clip_p(images=[_square(i) for i in imgs[s:s + 32]], return_tensors='pt').to(self.dev)
            with torch.no_grad():
                f = self.clip.get_image_features(**inp)
            f = f / f.norm(dim=-1, keepdim=True)
            p = (100.0 * f @ T.T).softmax(-1).float().cpu().numpy()
            res += [dict(zip(ks, map(float, row))) for row in p]
        return res


def _square(im):
    from PIL import Image
    w, h = im.size
    if w == h:
        return im
    s = max(w, h)
    bg = Image.new('RGB', (s, s), (124, 116, 104))
    bg.paste(im, ((s - w) // 2, (s - h) // 2))
    return bg


# ---------------------------------------------------------------- 2. 영역
def segment_ortho(M, rgb, valid, cell, tile=800, overlap=200, dbg=None):
    """타일마다 상자 → 영역 → CLIP 확인 → (종류 칸, 신뢰도 칸) + 연석 · 석축 AI 칸.
    겹치면 큰 영역 먼저 칠하고 작은 영역이 위에 덮음 (1126 시험: 타일 전체 «밭» 영역이 길가 마당 «콘크리트» 를 덮었음)."""
    import numpy as np
    from PIL import Image
    H, W = valid.shape
    acc = []                                              # (넓이, 종류 번호, 신뢰도, r, c, 조각 bool)
    cov = np.zeros((H, W), bool)                          # 어느 영역이든 칠한 곳 (보충 단계가 빈 곳 찾기)

    def paint(r0, c0, m, j, conf):
        ys, xs = np.nonzero(m)
        a, b, c, d = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        acc.append((int(m.sum()), j, float(conf), r0 + a, c0 + c, m[a:b, c:d].copy()))
        cov[r0 + a:r0 + b, c0 + c:c0 + d] |= m[a:b, c:d]
    curb_ai = np.zeros((H, W), bool)
    wall_ai = np.zeros((H, W), np.float16)
    step = tile - overlap
    tiles = [(r, c) for r in range(0, max(1, H - overlap), step) for c in range(0, max(1, W - overlap), step)]
    nmask = 0
    for ti, (r0, c0) in enumerate(tiles):
        say(10 + 30.0 * ti / len(tiles), '영역 찾기 · 타일 %d / %d' % (ti + 1, len(tiles)))
        r1, c1 = min(H, r0 + tile), min(W, c0 + tile)
        if valid[r0:r1, c0:c1].mean() < 0.05:
            continue
        sub = rgb[r0:r1, c0:c1].copy()
        sub[~valid[r0:r1, c0:c1]] = (124, 116, 104)
        img = Image.fromarray(sub)
        vt = int(valid[r0:r1, c0:c1].sum())
        dets = M.detect(img, DET_PROMPTS)
        # 길만 따로 한 번 더 (글이 많으면 좁은 농로 상자가 밀림 · 394장 시험) — 종류는 CLIP 이 포장 갈래 안에서 고름
        dets += [(b, s, 'road-only %s' % lab) for b, s, lab in M.detect(img, ROAD_PROMPTS, box_thr=0.2)]
        if not dets:
            continue
        masks, ious = M.segment(img, [d[0] for d in dets])
        crops, idx = [], []
        for k, ((b, s, lab), m) in enumerate(zip(dets, masks)):
            cls = _det_class(lab)
            m &= valid[r0:r1, c0:c1]
            if m.sum() < 200:                             # 0.5 m² 아래 (5 cm 칸)
                continue
            if cls == 'curb':                             # 연석 상자 = 근거만 (선은 DSM 으로)
                curb_ai[r0:r1, c0:c1] |= m
                continue
            crops.append(m); idx.append((k, cls, s, m, b, lab))
        probs = _mask_probs(M, sub, crops, cell)
        for (k, cls, s, m, b, lab), p in zip(idx, probs):
            if cls == 'wall':                             # 석축은 위에서 잘 안 보임 → 글 상자만 근거 칸에 (영역 경쟁에선 뺌)
                wr = wall_ai[r0:r1, c0:c1]
                np.maximum(wr, np.where(m, s, 0).astype(np.float16), out=wr)
            # 종류 = CLIP 확률 × 글 상자 갈래 무게 (같은 종류 1 · 같은 갈래 0.5 · 포장 갈래 상자에서만 «포장 아님» 0.25)
            #   포장 종류(보도 · 아스팔트 · 콘크리트 · 비포장)는 글 상자가 포장 갈래(도로 · 보도 · 주차장 …)일 때만 —
            #   1126 시험: 눈 덮인 밭 · 비닐하우스를 CLIP 이 «흙 · 자갈» 로 크게 봄 → 갈래가 다르면 포장 종류로 안 바꿈
            road = cls in AREA or cls == 'pave'
            pa = sum(p[kk] for kk in AREA)
            why = ''
            if road and pa >= 0.4:                        # 글 상자도 포장 · CLIP 도 포장 쪽 → 포장 종류 안에서만 고름 (마당: 차 · 지붕 섞여도)
                q = {kk: p[kk] * (1.0 if kk == cls or cls == 'pave' else 0.5) for kk in AREA}
                best = max(q, key=q.get)
                pb = p[best] / pa
                if m.sum() > 0.5 * vt:                    # 타일 절반 넘는 포장 = 타일 통째 영역 (394장 시험: 네모 조각) → 버림
                    why = '타일 통째'
            else:
                q = {kk: v * (1.0 if kk == cls else 0.5) for kk, v in p.items() if kk not in AREA and kk != 'wall'}
                best = max(q, key=q.get)
                pb = p[best]
                if pb < 0.5:                              # CLIP 도 확신 없음 → 안 칠함
                    why = '확신 낮음'
            if why:
                if dbg is not None:
                    dbg.append((r0, c0, b, lab + ' (버림 · %s)' % why, best, round(pb, 2), {kk: round(v, 2) for kk, v in p.items() if v > 0.05}))
                continue
            conf = min(1.0, 0.5 * s + 0.5 * pb * min(1.0, pa / 0.6 if best in AREA else 1.0) + (0.1 if best == cls else 0))
            paint(r0, c0, m, KEYS.index(best), conf)
            nmask += 1
            if dbg is not None:
                dbg.append((r0, c0, b, lab, best, round(conf, 2), {kk: round(v, 2) for kk, v in p.items() if v > 0.05}))
    # 보충: 글 상자가 못 덮은 곳 (1126 시험: 길가 마당 · 교차 도로) = SAM 점 격자 (2.5 m) → CLIP (더 엄하게)
    for ti, (r0, c0) in enumerate(tiles):
        say(40 + 15.0 * ti / len(tiles), '빈 곳 보충 · 타일 %d / %d' % (ti + 1, len(tiles)))
        r1, c1 = min(H, r0 + tile), min(W, c0 + tile)
        un = valid[r0:r1, c0:c1] & ~cov[r0:r1, c0:c1]
        if un.mean() < 0.03:
            continue
        g = 50
        pts = [(x, y) for y in range(g // 2, r1 - r0, g) for x in range(g // 2, c1 - c0, g) if un[y, x]][:240]
        if not pts:
            continue
        sub = rgb[r0:r1, c0:c1].copy()
        sub[~valid[r0:r1, c0:c1]] = (124, 116, 104)
        img = Image.fromarray(sub)
        got = []
        for m, iou in sorted(M.segment_points(img, pts), key=lambda x: -x[1]):
            m &= valid[r0:r1, c0:c1]
            a = int(m.sum())
            if iou < 0.8 or a < 800 or (m & un).sum() < 0.5 * a:
                continue
            if any((m & q).sum() > 0.6 * min(a, int(q.sum())) for q in got):
                continue
            got.append(m)
        for m, p in zip(got, _mask_probs(M, sub, got, cell)):
            q = {k: v for k, v in p.items() if k != 'wall'}
            best = max(q, key=q.get)
            if p[best] < (0.7 if best in AREA else 0.6) or (best in AREA and m.sum() > 0.5 * un.size):
                if dbg is not None:
                    ys, xs = np.nonzero(m)
                    dbg.append((r0, c0, [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())], 'sam-point-버림', best,
                                round(p[best], 2), {kk: round(v, 2) for kk, v in p.items() if v > 0.05}))
                continue
            if (m & un).any():
                paint(r0, c0, m & un, KEYS.index(best), 0.8 * p[best])
            nmask += 1
            if dbg is not None:
                ys, xs = np.nonzero(m)
                dbg.append((r0, c0, [int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())], 'sam-point', best, round(0.8 * p[best], 2),
                            {kk: round(v, 2) for kk, v in p.items() if v > 0.05}))
    lab = np.full((H, W), -1, np.int16)
    mx = np.zeros((H, W), np.float32)
    for area, j, conf, r, c, m in sorted(acc, key=lambda x: -x[0]):    # 큰 것 먼저 → 작은 것이 위에
        h, w = m.shape
        lab[r:r + h, c:c + w][m] = j
        mx[r:r + h, c:c + w][m] = conf
    return lab, mx, curb_ai, wall_ai, nmask, len(tiles)


def _mask_probs(M, sub, masks, cell, big_m2=60.0, car_m2=25.0):
    """영역마다 CLIP 확률 — 가린 바깥 그림 + (넓은 영역은) 안쪽 정사각 조각 셋까지 평균.
    1126 시험: 차 한 대가 선 마당 전체를 CLIP 이 «차» 0.85 로 봄 → 안쪽 조각 (바닥 결만) 을 함께 보고 · 25 m² 넘으면 «차» 아님."""
    import cv2
    import numpy as np
    from PIL import Image
    imgs, own = [], []
    for i, m in enumerate(masks):
        ys, xs = np.nonzero(m)
        a, b, c, d = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        cr = sub[a:b, c:d].copy(); cr[~m[a:b, c:d]] = (124, 116, 104)
        imgs.append(Image.fromarray(cr)); own.append(i)
        if m.sum() * cell * cell >= big_m2:
            # 타일 끝 바깥은 영역 아님 (0 으로 둘러 거리 셈 — 안 두르면 끝에서 조각이 밖으로 나가 빈 그림)
            dt = cv2.distanceTransform(np.pad(m.astype(np.uint8), 1), cv2.DIST_L2, 5)[1:-1, 1:-1]
            for _ in range(3):
                r, cc = np.unravel_index(int(dt.argmax()), dt.shape)
                h = int(min(dt[r, cc] * 0.7, 3.0 / cell))     # 반 변 ≤ 3 m · 영역 안에 다 들어감 (정사각 구석까지)
                if h * cell < 1.0:
                    break
                imgs.append(Image.fromarray(sub[max(0, r - h):r + h, max(0, cc - h):cc + h].copy())); own.append(i)
                dt[max(0, r - 2 * h):r + 2 * h, max(0, cc - 2 * h):cc + 2 * h] = 0
    ps = M.classify(imgs, CLIP_TOP)
    out = []
    for i, m in enumerate(masks):
        mine = [p for p, o in zip(ps, own) if o == i]
        p = {k: sum(q[k] for q in mine) / len(mine) for k in mine[0]}
        if m.sum() * cell * cell > car_m2 and p.get('car', 0) > 0:      # 차보다 큰 영역은 차가 아님 → 나머지로 다시 나눔
            p['car'] = 0.0
            t = sum(p.values()) or 1.0
            p = {k: v / t for k, v in p.items()}
        out.append(p)
    return out


def _det_class(lab):
    lab = lab.lower()
    if lab.startswith('road-only'):
        return 'pave'
    for p, c in DET_PROMPTS:
        if p in lab:
            return c
    for p, c in DET_PROMPTS:
        if any(w in lab for w in p.split()):
            return c
    return 'veg'


def label_map(lab0, mx, valid, cell, thr=0.2):
    """칠한 종류 칸 → 종류 번호 칸 (-1 = 없음) · 작은 구멍 · 작은 조각 정리."""
    import cv2
    import numpy as np
    lab = np.where((mx >= thr) & valid, lab0, -1).astype(np.int16)
    k = max(1, int(round(0.3 / cell)))
    ker = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * k + 1, 2 * k + 1))
    out = np.full_like(lab, -1)
    for j in range(len(KEYS)):
        m = (lab == j).astype(np.uint8)
        if not m.any():
            continue
        m = cv2.morphologyEx(cv2.morphologyEx(m, cv2.MORPH_OPEN, ker), cv2.MORPH_CLOSE, ker)
        n, cc, st, _ = cv2.connectedComponentsWithStats(m, connectivity=8)
        keep = np.zeros(n, bool); keep[1:] = st[1:, cv2.CC_STAT_AREA] * cell * cell >= 2.0
        out[keep[cc] & (out < 0)] = j
    return out, mx


# ---------------------------------------------------------------- 3. 영역 다각형
def area_features(lab, mx, dsm, x0, y1, cell, origin):
    import cv2
    import numpy as np
    feats = []
    gx0, gy1 = x0 - origin[0], y1 - origin[1]
    # 거칠기 = 3D 면 높이에서 0.4 m 흐린 면을 뺀 값 (포장은 매끈 · 갈아엎은 밭은 고랑) — 안쪽(가장자리 0.5 m 뺌) 중앙값
    Z = np.nan_to_num(dsm, nan=float(np.nanmedian(dsm)) if np.isfinite(dsm).any() else 0.0).astype(np.float32)
    hp = np.abs(Z - cv2.GaussianBlur(Z, (0, 0), 0.4 / cell))
    ek = np.ones((2 * int(0.5 / cell) + 1,) * 2, np.uint8)
    for cls in AREA:
        j = KEYS.index(cls)
        m = (lab == j).astype(np.uint8)
        if not m.any():
            continue
        cs, hier = cv2.findContours(m, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_NONE)
        for i, c in enumerate(cs):
            if hier[0][i][3] >= 0:                        # 구멍은 바깥 다각형 넓이에서 뺌
                continue
            outer = cv2.contourArea(c)
            holes = sum(cv2.contourArea(cs[h]) for h in range(len(cs)) if hier[0][h][3] == i)
            area = (outer - holes) * cell * cell
            if area < 2.0:
                continue
            ap = cv2.approxPolyDP(c, max(1.0, 0.15 / cell), True)[:, 0, :].astype(float)
            if len(ap) < 3:
                continue
            reg = np.zeros_like(m); cv2.drawContours(reg, cs, i, 1, -1)
            conf = float(mx[reg.astype(bool)].mean())
            inn = cv2.erode(reg, ek).astype(bool)
            rough = float(np.median(hp[inn if inn.sum() > 50 else reg.astype(bool)]))
            pts = _px_to_xyz(ap, dsm, gx0, gy1, cell)
            log('  %s %.0f m² · 신뢰도 %.2f · 거칠기 %.1f cm' % (cls, area, conf, 100 * rough))
            feats.append({'cls': cls, 'kind': 'area', 'pts': pts, 'height': None, 'conf': round(conf, 2), 'src': ['ortho', 'dsm'],
                          'attrs': {'state': '모름', 'material': MAT_NAME[cls], 'photo': None, 'area': round(area, 1)},
                          'status': 'ai', 'note': '', '_px': ap, '_rough': rough})
    return feats


def area_filter(feats, shape, cell, gap_m=3.0, thick_m=6.0, rough_m=0.025):
    """포장 영역 거르기 (394장 시험):
      거칠기 > 2.5 cm = 나무 · 덤불 · 가장자리 찌꺼기 (포장은 0.2~0.9 cm 로 잼)
      다른 포장과 3 m 넘게 떨어진 외딴 덩어리 + 뭉툭함 (두께 = 넓이 ÷ 둘레 절반 > 6 m) = 눈 덮인 밭 · 지붕
      (길 · 길가 마당은 길에 붙어 있고 · 농로는 가늘어서 남음). 버린 것 수를 돌려줌."""
    import cv2
    import numpy as np
    H, W = shape
    ids = np.zeros((H, W), np.int32)
    for i, f in enumerate(feats):
        cv2.fillPoly(ids, [np.round(f['_px']).astype(np.int32)], i + 1)
    k = int(round(gap_m / 2 / cell))
    n, cc = cv2.connectedComponents(cv2.dilate((ids > 0).astype(np.uint8), np.ones((2 * k + 1,) * 2, np.uint8)), connectivity=8)
    comp = np.zeros(len(feats), int)
    for i in range(len(feats)):
        ys, xs = np.nonzero(ids == i + 1)
        comp[i] = np.bincount(cc[ys, xs]).argmax() if len(ys) else -1
    out, drop = [], 0
    for i, f in enumerate(feats):
        per = cv2.arcLength(np.round(f['_px']).astype(np.float32)[:, None, :], True) * cell
        thick = f['attrs']['area'] / max(per / 2, 1e-6)
        alone = (comp == comp[i]).sum() == 1
        long_ = per / 2 >= 6.0 * thick                    # 길쭉함 (길이 ÷ 두께 ≥ 6 = 길) — 2차로 길은 두께 ≈ 폭 6~7 m (394장 시험)
        if f.get('_rough', 0) > rough_m or (alone and thick > thick_m and not long_):
            drop += 1
            log('  버림 %s %.0f m² · 거칠기 %.1f cm · 두께 %.1f m · %s' % (f['cls'], f['attrs']['area'], 100 * f.get('_rough', 0), thick,
                                                                 '외딴' if alone else '붙음'))
            continue
        out.append(f)
    return out, drop


def _px_to_xyz(ap, dsm, gx0, gy1, cell):
    import numpy as np
    H, W = dsm.shape
    out = []
    for c, r in ap:
        ci, ri = int(min(W - 1, max(0, c))), int(min(H - 1, max(0, r)))
        win = dsm[max(0, ri - 2):ri + 3, max(0, ci - 2):ci + 3]
        z = float(np.nanmedian(win)) if np.isfinite(win).any() else 0.0
        out.append([round(gx0 + (c + 0.5) * cell, 3), round(gy1 - (r + 0.5) * cell, 3), round(z, 3)])
    return out


def _bilinear(Z, r, c):
    import numpy as np
    H, W = Z.shape
    r = np.clip(r, 0, H - 1.001); c = np.clip(c, 0, W - 1.001)
    r0 = np.floor(r).astype(int); c0 = np.floor(c).astype(int); fr = r - r0; fc = c - c0
    return (Z[r0, c0] * (1 - fr) * (1 - fc) + Z[r0 + 1, c0] * fr * (1 - fc) + Z[r0, c0 + 1] * (1 - fr) * fc + Z[r0 + 1, c0 + 1] * fr * fc)


# ---------------------------------------------------------------- 4. 연석
def curb_lines(lab, mx, dsm, curb_ai, x0, y1, cell, origin, step_min=0.06, step_max=0.35):
    """포장 영역 경계 → 경계에 수직인 DSM 단면에서 턱 찾기 → 이어진 턱 = 연석 선."""
    import cv2
    import numpy as np
    H, W = lab.shape
    gx0, gy1 = x0 - origin[0], y1 - origin[1]
    bad = np.isin(lab, [KEYS.index('car'), KEYS.index('roof')])
    taken = np.zeros((H // 5 + 2, W // 5 + 2), bool)             # 이미 쓴 자리 (25 cm 칸) — 보도 · 차도 경계를 두 번 안 셈
    sp = max(1, int(round(0.25 / cell)))                          # 경계 표본 간격 25 cm
    t = np.arange(-0.8, 0.8001, 0.05) / cell                       # 단면 ±0.8 m (칸)
    feats = []
    curb_near = cv2.dilate(curb_ai.astype(np.uint8), np.ones((int(1.0 / cell) | 1,) * 2, np.uint8)).astype(bool)
    for cls in ['sidewalk', 'asphalt', 'concrete']:
        m = (lab == KEYS.index(cls)).astype(np.uint8)
        if not m.any():
            continue
        cs, _ = cv2.findContours(m, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
        for c in cs:
            c = c[:, 0, :].astype(float)
            if len(c) < 8 * sp:
                continue
            c = c[::sp]
            sm = np.stack([np.convolve(np.r_[c[-3:, k], c[:, k], c[:3, k]], np.ones(7) / 7, 'valid') for k in (0, 1)], 1)
            tg = np.roll(sm, -1, 0) - np.roll(sm, 1, 0)
            nrm = np.stack([tg[:, 1], -tg[:, 0]], 1); nrm /= np.linalg.norm(nrm, axis=1, keepdims=True) + 1e-9
            C = c[:, 0][:, None] + nrm[:, 0][:, None] * t[None, :]; R = c[:, 1][:, None] + nrm[:, 1][:, None] * t[None, :]
            Z = _bilinear(np.nan_to_num(dsm, nan=-999.0), R, C)
            okz = (Z > -900).all(1)
            g = np.abs(Z[:, 3:] - Z[:, :-3])                     # 15 cm 사이 높이 차
            k = g.argmax(1) + 1                                  # 턱 자리 (단면 번호)
            n_t = len(t)
            lo = np.zeros(len(c)); hi = np.zeros(len(c)); stp = np.zeros(len(c)); top_t = np.zeros(len(c))
            for i in range(len(c)):
                a = Z[i, max(0, k[i] - 8):max(1, k[i] - 2)]; b = Z[i, min(n_t - 1, k[i] + 4):min(n_t, k[i] + 10)]
                if len(a) < 2 or len(b) < 2:
                    continue
                za, zb = np.median(a), np.median(b)
                stp[i] = abs(zb - za); lo[i] = min(za, zb); hi[i] = max(za, zb)
                # 턱 자리 = 그 둘레 한 칸 높이 차의 무게 가운데 (선 = 연석 앞면 위 모서리 · 인공 시험: 맨 앞 창을 쓰면 12 cm 밀림)
                g1 = np.abs(np.diff(Z[i, max(0, k[i] - 3):min(n_t, k[i] + 5)]))
                tm = (t[max(0, k[i] - 3):][:len(g1)] + t[max(0, k[i] - 3) + 1:][:len(g1)]) / 2
                top_t[i] = float((g1 * tm).sum() / g1.sum()) if g1.sum() > 0 else t[k[i]]
                flat = max(np.ptp(a), np.ptp(b))
                if flat > 0.6 * stp[i]:                          # 양쪽이 평평하지 않음 (비탈 · 나무) → 턱 아님
                    stp[i] = 0
            hit = okz & (stp >= step_min) & (stp <= step_max) & (g.max(1) >= 0.5 * stp)
            # 턱 위치 (위 모서리) 칸 좌표
            pc = c[:, 0] + nrm[:, 0] * top_t; pr = c[:, 1] + nrm[:, 1] * top_t
            ri = np.clip(pr.astype(int), 0, H - 1); ci = np.clip(pc.astype(int), 0, W - 1)
            hit &= ~bad[ri, ci]
            for i in np.nonzero(hit)[0]:
                if taken[ri[i] // 5, ci[i] // 5]:
                    hit[i] = False
            # 이어진 구간 (틈 3표본 = 0.75 m 까지 이음)
            idx = np.nonzero(hit)[0]
            if len(idx) == 0:
                continue
            runs, cur = [], [idx[0]]
            for a, b in zip(idx[:-1], idx[1:]):
                if b - a <= 4:
                    cur.append(b)
                else:
                    runs.append(cur); cur = [b]
            runs.append(cur)
            if len(runs) > 1 and runs[0][0] == 0 and runs[-1][-1] == len(c) - 1:   # 닫힌 경계의 처음 · 끝 이음
                runs[0] = runs[-1] + runs[0]; runs.pop()
            for run in runs:
                run = np.array(run)
                L = len(run) * sp * cell
                if L < 2.0:
                    continue
                full = np.arange(run[0], run[-1] + 1) if run[-1] >= run[0] else np.r_[np.arange(run[0], len(c)), np.arange(0, run[-1] + 1)]
                frac = len(run) / max(1, len(full))
                at = {v: i for i, v in enumerate(full)}
                xp = np.array([at[v] for v in run], float)      # 구간 안 자리 (닫힌 경계 이음도 맞게)
                tt = np.interp(np.arange(len(full)), xp, top_t[run])
                tt = np.convolve(np.r_[[tt[0]] * 2, tt, [tt[-1]] * 2], np.ones(5) / 5, 'valid')     # 턱 자리 흔들림 줄임
                zt = np.interp(np.arange(len(full)), xp, hi[run])                                    # 연석 위 높이 (턱 위 평평한 면)
                px = np.stack([c[full, 0] + nrm[full, 0] * tt, c[full, 1] + nrm[full, 1] * tt], 1)
                ap = cv2.approxPolyDP(px.astype(np.float32)[:, None, :], max(0.5, 0.05 / cell), False)[:, 0, :].astype(float)
                if len(ap) < 2:
                    continue
                pts = _px_to_xyz(ap, dsm, gx0, gy1, cell)
                for p_, q in zip(pts, ap):                      # 높이 = 그 자리 연석 위 (단면에서 잰 높은 쪽)
                    p_[2] = round(float(zt[int(np.argmin(np.hypot(px[:, 0] - q[0], px[:, 1] - q[1])))]), 3)
                h = float(np.median(stp[run]))
                ai = float(curb_near[ri[run], ci[run]].mean())
                conf = min(1.0, 0.35 + 0.35 * frac + 0.2 * float(np.mean(mx[ri[run], ci[run]])) + 0.15 * ai)
                for i in full:
                    taken[max(0, ri[i] // 5 - 1):ri[i] // 5 + 2, max(0, ci[i] // 5 - 1):ci[i] // 5 + 2] = True
                feats.append({'cls': 'curb', 'kind': 'line', 'pts': pts, 'height': round(h, 2), 'conf': round(conf, 2),
                              'src': ['ortho', 'dsm'],
                              'attrs': {'state': '모름', 'material': '', 'photo': None, 'length': round(_len(pts), 1),
                                        'side': MAT_NAME.get(cls, cls)},
                              'status': 'ai', 'note': '%s 경계 턱%s' % (MAT_NAME.get(cls, cls), ' · AI 연석 상자 겹침' if ai > 0.3 else ''),
                              '_px': ap})
    return feats


def _len(pts):
    return sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts[:-1], pts[1:]))


# ---------------------------------------------------------------- 5. 석축
def wall_lines(lab, dsm, wall_ai, valid, x0, y1, cell, origin, dtm=None, near=None, min_h=0.8, max_h=8.0, slope=1.5):
    """DSM 급경사 띠 → 위 · 아래 선 (둘 다 cls wall · attrs.edge top/bottom · 수량은 top 만).
    dtm = 땅만 남긴 높이 칸 (있으면 거기서도 턱이 있어야 — 비닐하우스 · 건물 · 차 가장자리는 DTM 에선 평평)
    near = 포장 영역 둘레 (이 안에 걸친 것만 — 관로 공사 부대공)"""
    import cv2
    import numpy as np
    H, W = dsm.shape
    gx0, gy1 = x0 - origin[0], y1 - origin[1]
    Z = np.nan_to_num(dsm, nan=np.nanmedian(dsm)).astype(np.float32)
    Zs = cv2.GaussianBlur(Z, (0, 0), 0.1 / cell)
    gy, gx = np.gradient(Zs, cell)
    steep = (np.hypot(gx, gy) > slope) & np.isfinite(dsm)
    k = max(1, int(round(0.15 / cell)))
    steep = cv2.morphologyEx(steep.astype(np.uint8), cv2.MORPH_CLOSE, np.ones((2 * k + 1,) * 2, np.uint8))
    n, cc, st, _ = cv2.connectedComponentsWithStats(steep, connectivity=8)
    bad = np.isin(lab, [KEYS.index('veg'), KEYS.index('roof'), KEYS.index('car')])
    ek = int(round(0.5 / cell))                           # 결과물 끝(잘라낸 경계)에서 0.5 m 안쪽만 — 끝에서 DSM 이 뚝 떨어지는 것은 벽이 아님
    inner = cv2.erode(valid.astype(np.uint8), np.ones((2 * ek + 1,) * 2, np.uint8)).astype(bool)
    feats = []
    dk = max(1, int(round(0.25 / cell)))
    ker = np.ones((2 * dk + 1,) * 2, np.uint8)
    for j in range(1, n):
        if st[j, cv2.CC_STAT_AREA] * cell * cell < 0.5:
            continue
        x, y, w, h = st[j, :4]
        a0, b0 = max(0, y - 3 * dk), min(H, y + h + 3 * dk); c0, d0 = max(0, x - 3 * dk), min(W, x + w + 3 * dk)
        m = (cc[a0:b0, c0:d0] == j).astype(np.uint8)
        md = cv2.dilate(m, ker)
        if inner[a0:b0, c0:d0][md.astype(bool)].mean() < 0.7:     # 거의 끝에 붙은 띠만 버림 (경계를 지나는 진짜 벽은 남김)
            continue
        ring = md.astype(bool) & ~m.astype(bool)
        zr = Zs[a0:b0, c0:d0][ring]
        if len(zr) < 10:
            continue
        zhi, zlo = np.percentile(zr, 90), np.percentile(zr, 10)
        relief = zhi - zlo
        if relief < min_h or relief > max_h:
            continue
        # 길쭉함 = 두께(넓이 ÷ 둘레 절반) — 굽은 벽도 됨 (최소 사각형 폭은 굽으면 커짐 · 인공 시험) · 나무 · 덩어리는 두꺼움
        mc, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        Lr = sum(cv2.arcLength(q, True) for q in mc) / 2 * cell
        Wr = float(m.sum()) * cell * cell / max(Lr, 1e-6)
        if Lr < 2.0 or Wr > max(0.8, 0.5 * relief) or Lr < 4.0 * Wr:
            continue
        badf = bad[a0:b0, c0:d0][md.astype(bool)].mean()
        if badf > 0.5:                                    # 나무 · 건물 · 차 가장자리
            continue
        if near is not None and not near[a0:b0, c0:d0][md.astype(bool)].any():
            continue
        cs, _ = cv2.findContours(md, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        cont = max(cs, key=len)[:, 0, :]
        zc = Zs[a0:b0, c0:d0][cont[:, 1], cont[:, 0]]
        top = zc > (zhi + zlo) / 2
        runs = _circ_runs(top)
        best = {}
        for v, ids in runs:
            L = len(ids) * cell
            if L >= 1.5 and L > best.get(v, (0, None))[0]:
                best[v] = (L, ids)
        if True not in best or False not in best:
            continue
        if dtm is not None:                               # 땅 높이(DTM)에서도 위 · 아래 차가 있어야 땅의 턱 (394장 시험: 비닐하우스 가장자리)
            pt_ = cont[best[True][1]] + [c0, a0]; pb_ = cont[best[False][1]] + [c0, a0]
            zt_ = np.nanmedian(dtm[pt_[:, 1], pt_[:, 0]]) if np.isfinite(dtm[pt_[:, 1], pt_[:, 0]]).any() else np.nan
            zb_ = np.nanmedian(dtm[pb_[:, 1], pb_[:, 0]]) if np.isfinite(dtm[pb_[:, 1], pb_[:, 0]]).any() else np.nan
            if np.isfinite(zt_) and np.isfinite(zb_) and zt_ - zb_ < 0.4:
                continue
        aiw = float(wall_ai[a0:b0, c0:d0][md.astype(bool)].astype(np.float32).max())
        conf = min(1.0, 0.4 + 0.1 * min(3.0, relief) + 0.3 * aiw - 0.3 * badf)
        pair = []
        for v, edge in ((True, 'top'), (False, 'bottom')):
            ids = best[v][1]
            px = cont[ids].astype(float) + [c0, a0]
            ap = cv2.approxPolyDP(px.astype(np.float32)[:, None, :], max(0.5, 0.08 / cell), False)[:, 0, :].astype(float)
            pts = _px_to_xyz(ap, Zs, gx0, gy1, cell)
            f = {'cls': 'wall', 'kind': 'line', 'pts': pts, 'height': round(float(relief), 2), 'conf': round(conf, 2),
                 'src': ['dsm'] + (['ortho'] if aiw > 0.2 else []),
                 'attrs': {'state': '모름', 'material': '', 'photo': None, 'edge': edge, 'length': round(_len(pts), 1)},
                 'status': 'ai', 'note': '석축 %s 선%s' % ('위' if edge == 'top' else '아래', ' (수량은 위 선으로)' if edge == 'bottom' else ''),
                 '_px': ap, '_pair': j}
            pair.append(f)
        feats += pair
    return feats


def _circ_runs(v):
    """닫힌 줄의 같은 값 구간 [(값, 번호들)] (처음 · 끝 이음)."""
    import numpy as np
    n = len(v)
    if n == 0:
        return []
    if v.all() or (~v).all():
        return [(bool(v[0]), np.arange(n))]
    s = int(np.nonzero(v != np.roll(v, 1))[0][0])
    order = np.r_[np.arange(s, n), np.arange(0, s)]
    out, cur = [], [order[0]]
    for i in order[1:]:
        if v[i] == v[cur[-1]]:
            cur.append(i)
        else:
            out.append((bool(v[cur[0]]), np.array(cur))); cur = [i]
    out.append((bool(v[cur[0]]), np.array(cur)))
    return out


# ---------------------------------------------------------------- 6. 360 근거
class Pano360:
    """360 프로젝트 (data/project.js · data/pano/NNNN.js · data/calib.js) — 북쪽 정렬 등장방형 (viewer/app.js 머리말과 같은 식)."""

    def __init__(self, folder):
        import numpy as np
        self.folder = folder
        t = open(os.path.join(folder, 'data', 'project.js'), encoding='utf-8').read()
        d = json.loads(t[t.index('{'):t.rindex('}') + 1])
        self.E0, self.N0 = float(d['origin']['E']), float(d['origin']['N'])
        cal = {}
        cp = os.path.join(folder, 'data', 'calib.js')
        if os.path.exists(cp):
            ct = open(cp, encoding='utf-8').read()
            try:
                cal = json.loads(ct[ct.index('{'):ct.rindex('}') + 1]).get('videos', {})
            except ValueError:
                cal = {}
        self.shots = []
        for s in d.get('shots', []):
            c = cal.get(s.get('video'), {}) or {}
            self.shots.append({'id': s['id'], 'E': s['e'] + c.get('dx', 0) + self.E0, 'N': s['n'] + c.get('dy', 0) + self.N0,
                               'Z': s['z'] + c.get('dz', 0), 'R': _rot(c), 'file': s.get('file') or 'data/pano/%s.js' % s['id']})
        self.xy = np.array([[s['E'], s['N']] for s in self.shots]) if self.shots else np.zeros((0, 2))
        self._cache = {}

    def pick(self, E, N, best=14.0, rmin=4.0, rmax=40.0, k=2):
        """땅 점을 잘 보는 사진 (가까우면 선명 · 너무 가까우면 바로 아래라 옆면이 안 보임 → 14 m 쯤이 좋음)."""
        import numpy as np
        if not len(self.xy):
            return []
        d = np.hypot(self.xy[:, 0] - E, self.xy[:, 1] - N)
        ok = np.nonzero((d >= rmin) & (d <= rmax))[0]
        return [int(i) for i in ok[np.argsort(np.abs(d[ok] - best))][:k]]

    def image(self, i):
        import numpy as np
        from PIL import Image
        Image.MAX_IMAGE_PIXELS = None
        if i not in self._cache:
            if len(self._cache) > 6:
                self._cache.pop(next(iter(self._cache)))
            t = open(os.path.join(self.folder, self.shots[i]['file']), encoding='utf-8').read()
            m = re.search(r'base64,([A-Za-z0-9+/=]+)', t)
            self._cache[i] = np.asarray(Image.open(io.BytesIO(base64.b64decode(m.group(1)))).convert('RGB'))
        return self._cache[i]

    def view(self, i, E, N, Z, size=384, half_m=3.0):
        """사진 i 에서 땅 점 (E, N, Z) 쪽 평면 조각 (화각 = 점 둘레 ±half_m 가 들어오게 · 30~90°)."""
        import cv2
        import numpy as np
        from PIL import Image
        s = self.shots[i]
        w = np.array([E - s['E'], N - s['N'], Z - s['Z']], float)
        dist = np.linalg.norm(w)
        f = w / dist
        fov = math.degrees(2 * math.atan(half_m / max(1.0, dist)))
        fov = max(15.0, min(90.0, fov))
        up = np.array([0, 0, 1.0])
        r = np.cross(f, up); r /= np.linalg.norm(r) + 1e-12
        u = np.cross(r, f)
        tn = math.tan(math.radians(fov) / 2)
        g = (np.arange(size) + 0.5) / size * 2 - 1
        X, Y = np.meshgrid(g * tn, -g * tn)
        D = f[None, None, :] + X[..., None] * r[None, None, :] + Y[..., None] * u[None, None, :]
        D = D @ s['R']                                      # 사진 방향 p = Rᵀ·w (행 벡터라 D·R)
        D /= np.linalg.norm(D, axis=2, keepdims=True)
        lam = np.arctan2(D[..., 0], D[..., 1]); phi = np.arcsin(np.clip(D[..., 2], -1, 1))
        img = self.image(i)
        Hh, Ww = img.shape[:2]
        mx = ((lam + math.pi) / (2 * math.pi) * Ww - 0.5).astype(np.float32)
        my = ((math.pi / 2 - phi) / math.pi * Hh - 0.5).astype(np.float32)
        out = cv2.remap(img, mx, my, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)
        return Image.fromarray(out), dist


def _rot(c):
    """R = Rz(dyaw)·Rx(dpitch)·Ry(droll) (app.js rot64 와 같음)."""
    import numpy as np
    a, b, g = (math.radians(c.get(k, 0.0)) for k in ('dyaw', 'dpitch', 'droll'))
    Rz = np.array([[math.cos(a), math.sin(a), 0], [-math.sin(a), math.cos(a), 0], [0, 0, 1]])
    Rx = np.array([[1, 0, 0], [0, math.cos(b), -math.sin(b)], [0, math.sin(b), math.cos(b)]])
    Ry = np.array([[math.cos(g), 0, math.sin(g)], [0, 1, 0], [-math.sin(g), 0, math.cos(g)]])
    return Rz @ Rx @ Ry


def evidence_360(M, P, feats, origin, dbg_dir=None):
    """후보마다 360 사진 조각 → CLIP 재질 · 상태 · (연석 · 석축은 있는지)."""
    import numpy as np
    n_used = 0
    for fi, f in enumerate(feats):
        if fi % 10 == 0:
            say(80 + 15.0 * fi / max(1, len(feats)), '360 사진 근거 · %d / %d' % (fi + 1, len(feats)))
        pts = np.array(f['pts'], float)
        if f['kind'] == 'area':                           # 넓은 영역 = 안쪽 점 여럿 중 사진에 가장 알맞은 곳
            cand = _inner_points(pts)
        else:
            mid = len(pts) // 2
            cand = [pts[mid]]
        best = None
        for q in cand:
            E, N, Z = q[0] + origin[0], q[1] + origin[1], q[2] + origin[2]
            ids = P.pick(E, N)
            if ids:
                d = math.hypot(P.shots[ids[0]]['E'] - E, P.shots[ids[0]]['N'] - N)
                if best is None or abs(d - 14) < best[0]:
                    best = (abs(d - 14), (E, N, Z), ids)
        if best is None:                                  # 40 m 안에 360 사진 없음 → 재질 · 상태 모름 (화면에서 사람이 확인)
            f['note'] = (f['note'] + ' · 360 사진 없음').strip(' ·')
            continue
        (E, N, Z), ids = best[1], best[2]
        views = []
        for i in ids:
            try:
                im, _ = P.view(i, E, N, Z, half_m=2.5 if f['cls'] != 'wall' else max(3.0, (f.get('height') or 2) * 1.5))
                views.append((i, im))
            except Exception as ex:                       # noqa: BLE001 — 사진 파일 깨짐 · 없음
                log('  360 사진 %s 못 읽음 (%s)' % (P.shots[i]['id'], ex))
        if not views:
            continue
        ims = [v[1] for v in views]
        a = f['attrs']
        if f['cls'] in AREA:
            pm = _avg(M.classify(ims, CLIP_SIDE_MAT))
            mat = max(pm, key=pm.get)
            if mat in AREA and mat != f['cls'] and pm[mat] >= 0.6:
                # 재질은 360 이 정함 (옆에서 가까이 본 결 · 1126 시험: 위에서 «콘크리트» 로 본 길가 마당 = 360 흙 · 자갈 0.90 이 맞음)
                f['note'] = (f['note'] + ' 정사는 %s 로 봄 → 360 으로 바꿈' % MAT_NAME[f['cls']]).strip()
                f['cls'] = mat
                f['conf'] = round(f['conf'] * 0.9, 2)
            elif mat != f['cls'] and pm[mat] > 0.6:       # 360 이 밭 · 풀로 봄 = 포장 아님
                f['conf'] = round(f['conf'] * 0.5, 2)
                f['note'] = (f['note'] + ' 360 은 %s 로 봄' % MAT_NAME[mat]).strip()
            elif mat == f['cls']:
                f['conf'] = round(min(1.0, f['conf'] + 0.1), 2)
            a['material'] = MAT_NAME[f['cls']] + (' (360: %s)' % MAT_NAME[mat] if mat != f['cls'] else '')
            ps = _avg(M.classify(ims, _state_table(MAT_EN[f['cls']])))
            st = max(ps, key=ps.get)
            a['state'] = st if ps[st] >= 0.45 else '모름'
            a['p360'] = {k: round(v, 2) for k, v in list(pm.items()) + [('state_' + k, v) for k, v in ps.items()]}
        elif f['cls'] == 'curb':
            pc = _avg(M.classify(ims, CLIP_CURB))
            ps = _avg(M.classify(ims, _state_table('curb stone')))
            m = max(pc, key=pc.get)
            a['material'] = CURB_MAT[m]
            f['conf'] = round(min(1.0, max(0.05, f['conf'] + 0.15 * (2 * pc[m] - 1))), 2) if m != 'none' else                 round(max(0.05, min(f['conf'] - 0.15, 0.25 if pc[m] >= 0.5 else 1.0)), 2)   # 360 에 연석이 안 보이면 버림
            st = max(ps, key=ps.get)
            a['state'] = st if ps[st] >= 0.5 else '모름'
            a['p360'] = {k: round(v, 2) for k, v in pc.items()}
        elif f['cls'] == 'wall':
            pw = _avg(M.classify(ims, CLIP_WALL))
            m = max(pw, key=pw.get)
            a['material'] = WALL_MAT[m]
            f['conf'] = round(min(1.0, max(0.05, f['conf'] + 0.15 * (2 * pw[m] - 1))), 2) if m != 'slope' else                 round(max(0.05, min(f['conf'] - 0.15, 0.25 if pw[m] >= 0.5 else 1.0)), 2)   # 360 에 벽이 안 보이면 (흙 비탈 · 둑) 버림
            a['p360'] = {k: round(v, 2) for k, v in pw.items()}
        a['photo'] = P.shots[views[0][0]]['id']
        if '360' not in f['src']:
            f['src'].append('360')
        n_used += 1
        if dbg_dir and n_used <= 60:
            ims[0].save(os.path.join(dbg_dir, '360_%s_%s_%s.jpg' % (f['cls'], fi, P.shots[views[0][0]]['id'])), quality=85)
    return n_used


def _avg(ps):
    ks = ps[0].keys()
    return {k: sum(p[k] for p in ps) / len(ps) for k in ks}


def _inner_points(pts, k=40, edge_m=1.0):
    """다각형 안쪽 점들 (가장자리에서 edge_m 넘게 들어간 격자점 · 최대 k 개 + 대표점) — 높이는 가까운 꼭짓점 높이.
    394장 시험: 굽은 긴 길은 꼭짓점 평균이 길 밖(밭)에 떨어져 360 이 밭을 보고 판정했음."""
    import numpy as np
    from shapely.geometry import Point, Polygon
    poly = Polygon(pts[:, :2]).buffer(0)
    if poly.is_empty:
        return [pts.mean(0)]
    x0, y0, x1, y1 = poly.bounds
    step = max(2.0, math.sqrt(poly.area / k))
    out = []
    for x in np.arange(x0 + step / 2, x1, step):
        for y in np.arange(y0 + step / 2, y1, step):
            q = Point(x, y)
            if poly.contains(q) and poly.exterior.distance(q) >= edge_m:
                out.append((x, y))
    rp = poly.representative_point()
    out = [(rp.x, rp.y)] + out[:k]
    z = lambda x, y: pts[int(np.argmin(np.hypot(pts[:, 0] - x, pts[:, 1] - y))), 2]   # noqa: E731
    return [np.array([x, y, z(x, y)]) for x, y in out]


# ---------------------------------------------------------------- 확인 그림
COLORS = {'sidewalk': (255, 170, 0), 'asphalt': (90, 90, 255), 'concrete': (0, 220, 220), 'unpaved': (180, 120, 60),
          'wall': (255, 0, 255), 'veg': (40, 160, 40), 'field': (200, 200, 60), 'roof': (120, 120, 120), 'car': (255, 255, 255), 'water': (0, 90, 200)}


def draw_check(path, rgb, lab, feats, cell):
    import cv2
    import numpy as np
    from PIL import Image, ImageDraw
    over = rgb.astype(np.float32).copy()
    for j, k in enumerate(KEYS):                          # 부대공 아닌 종류 = 옅게 (AI 영역 칸 그대로)
        m = lab == j
        if m.any() and k in ['veg', 'field', 'roof', 'car']:
            over[m] = over[m] * 0.75 + np.array(COLORS[k], np.float32) * 0.25
    for f in feats:                                       # 포장 = 남은 후보 다각형만 (거른 것 · 버린 것은 안 칠함)
        if f['kind'] == 'area':
            m = np.zeros(lab.shape, np.uint8)
            cv2.fillPoly(m, [np.round(f['_px']).astype(np.int32)], 1)
            m = m.astype(bool)
            over[m] = over[m] * 0.55 + np.array(COLORS[f['cls']], np.float32) * 0.45
    im = Image.fromarray(over.clip(0, 255).astype(np.uint8))
    s = 1 if max(im.size) <= 3000 else 0.5
    if s != 1:
        im = im.resize((int(im.size[0] * s), int(im.size[1] * s)))
    dr = ImageDraw.Draw(im)
    for f in feats:
        px = [(float(x) * s, float(y) * s) for x, y in f['_px']]
        if f['kind'] == 'area':
            dr.polygon(px, outline=COLORS[f['cls']])
        elif f['cls'] == 'curb':
            dr.line(px, fill=(255, 0, 0), width=3)
        elif f['cls'] == 'wall':
            dr.line(px, fill=(255, 0, 255) if f['attrs'].get('edge') == 'top' else (0, 200, 255), width=4)
    # 범례
    y = 8
    for k, nm in [('sidewalk', '보도블록'), ('asphalt', '아스팔트'), ('concrete', '콘크리트'), ('unpaved', '비포장'), ('veg', '나무 · 풀'), ('field', '밭'),
                  ('roof', '건물'), ('car', '차')]:
        dr.rectangle([8, y, 26, y + 14], fill=COLORS[k]); dr.text((32, y), k, fill=(255, 255, 255)); y += 18
    dr.line([(8, y + 7), (26, y + 7)], fill=(255, 0, 0), width=3); dr.text((32, y), 'curb', fill=(255, 255, 255)); y += 18
    dr.line([(8, y + 7), (26, y + 7)], fill=(255, 0, 255), width=4); dr.text((32, y), 'wall top', fill=(255, 255, 255)); y += 18
    dr.line([(8, y + 7), (26, y + 7)], fill=(0, 200, 255), width=4); dr.text((32, y), 'wall bottom', fill=(255, 255, 255))
    im.save(path)


# ---------------------------------------------------------------- 실행
def run(a):
    import cv2
    import numpy as np
    origin = [float(v) for v in a.origin.split(',')]
    models = json.loads(a.models)
    dbg = a.debug
    if dbg:
        os.makedirs(dbg, exist_ok=True)
    tm = {}
    say(1, 'AI 모델 읽기')
    t = time.time()
    M = Models(models)
    tm['모델 읽기'] = time.time() - t
    log('모델 읽음 · %s · %s' % (M.dev, M.torch.cuda.get_device_name(0) if M.dev == 'cuda' else 'CPU'))
    say(5, '정사 · 3D 면 읽기')
    t = time.time()
    rgb, valid, x0, y1, c0, _ = read_ortho(a.project, a.cell)
    dsm, dsm_src = mesh_dsm(a.project, origin, x0, y1, a.cell, valid.shape)
    tm['정사 · DSM'] = time.time() - t
    log('정사 %d×%d (원래 %.1f cm → %.1f cm) · DSM = %s · 빈칸 %.1f%%' % (
        valid.shape[1], valid.shape[0], 100 * c0, 100 * a.cell, dsm_src, 100 * float(np.isnan(dsm)[valid].mean())))
    t = time.time()
    dl = [] if dbg else None
    lab0, mx0, curb_ai, wall_ai, nmask, ntile = segment_ortho(M, rgb, valid, a.cell, dbg=dl)
    tm['영역 찾기'] = time.time() - t
    lab, mx = label_map(lab0, mx0, valid, a.cell)
    cover = {k: round(float((lab == j).sum()) * a.cell ** 2, 1) for j, k in enumerate(KEYS)}
    log('영역 %d개 (타일 %d) · 넓이 m² %s' % (nmask, ntile, cover))
    say(58, '영역 다각형 · 연석 · 석축')
    t = time.time()
    feats = area_features(lab, mx, dsm, x0, y1, a.cell, origin)
    feats, nd = area_filter(feats, valid.shape, a.cell)
    log('포장 영역 %d개 (거침 · 외딴 뭉툭 %d개 버림)' % (len(feats), nd))
    if np.isfinite(dsm).any():
        curbs = curb_lines(lab, mx, dsm, curb_ai, x0, y1, a.cell, origin)
        dg = read_grid(os.path.join(a.project, '결과물', 'DTM.tif'), x0, y1, a.cell, valid.shape)
        paved = np.isin(lab, [KEYS.index(k) for k in AREA]).astype(np.uint8)
        near = (cv2.distanceTransform(1 - paved, cv2.DIST_L2, 5) * a.cell <= NEAR_M) if paved.any() else None
        log('석축 거르기: DTM %s · 포장 둘레 %s' % ('있음 (%.2f m)' % dg[1] if dg else '없음', '%d m' % NEAR_M if near is not None else '없음 (포장 못 찾음 → 전부)'))
        walls = wall_lines(lab, dsm, wall_ai, valid, x0, y1, a.cell, origin, dtm=dg[0] if dg else None, near=near)
    else:                                                 # 높이 자료 없음 → 영역만
        log('높이 자료(3D 면 · 지면.tif) 없음 → 연석 · 석축 선은 못 찾음')
        curbs, walls = [], []
    feats += curbs + walls
    tm['선 찾기'] = time.time() - t
    log('영역 다각형 %d · 연석 선 %d (%.0f m) · 석축 선 %d' % (len(feats) - len(curbs) - len(walls), len(curbs),
                                                     sum(f['attrs']['length'] for f in curbs), len(walls)))
    n360 = 0
    if a.p360:
        say(80, '360 사진 근거')
        t = time.time()
        try:
            P = Pano360(a.p360)
            inside = [s for s in P.shots if x0 - 40 < s['E'] < x0 + valid.shape[1] * a.cell + 40 and y1 - valid.shape[0] * a.cell - 40 < s['N'] < y1 + 40]
            log('360 사진 %d장 중 매핑 범위 (±40 m) %d장' % (len(P.shots), len(inside)))
            if inside:
                n360 = evidence_360(M, P, feats, origin, dbg)
        except Exception as ex:                           # noqa: BLE001 — 360 이 안 돼도 정사 · DSM 결과는 남김
            log('360 근거 실패 (%s) → 정사 · DSM 결과만' % ex)
        tm['360 근거'] = time.time() - t
        log('360 근거 붙인 후보 %d / %d' % (n360, len(feats)))
    pairs = {}
    for f in feats:                                       # 석축 위 · 아래 선은 같은 신뢰도 (하나만 남지 않게)
        if '_pair' in f:
            pairs.setdefault(f['_pair'], []).append(f)
    for fs in pairs.values():
        c = round(sum(f['conf'] for f in fs) / len(fs), 2)
        for f in fs:
            f['conf'] = c
    n_all = len(feats)
    feats = [f for f in feats if f['conf'] >= MIN_CONF]
    log('신뢰도 %.2f 아래 %d개 버림 → 후보 %d' % (MIN_CONF, n_all - len(feats), len(feats)))
    if dbg:
        draw_check(os.path.join(dbg, '확인_정사.png'), rgb, lab, feats, a.cell)
        with open(os.path.join(dbg, 'dets.json'), 'w', encoding='utf-8') as fp:
            json.dump(dl, fp, ensure_ascii=False)
    for f in feats:
        f.pop('_px', None); f.pop('_pair', None); f.pop('_rough', None)
    gpu = {}
    if M.dev == 'cuda':
        gpu = {'name': M.torch.cuda.get_device_name(0), 'peak_gb': round(M.torch.cuda.max_memory_allocated() / 1e9, 2)}
    res = {'features': feats, 'run': {'sec': {k: round(v, 1) for k, v in tm.items()}, 'total_sec': round(time.time() - T0, 1),
                                      'device': M.dev, 'gpu': gpu, 'cell': a.cell, 'dsm': dsm_src, 'masks': nmask, 'tiles': ntile,
                                      'cover_m2': cover, 'n360': n360, 'models': models}}
    with open(a.out, 'w', encoding='utf-8') as fp:
        json.dump(res, fp, ensure_ascii=False)
    log('끝 · 후보 %d · 시간 %s · 그래픽 메모리 최대 %s GB' % (len(feats), res['run']['sec'], gpu.get('peak_gb', '-')))


def main(argv=None):
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest='cmd')
    f = sub.add_parser('fetch')
    f.add_argument('--models', required=True)
    r = sub.add_parser('run')
    r.add_argument('--project', required=True)
    r.add_argument('--origin', required=True)
    r.add_argument('--out', required=True)
    r.add_argument('--cell', type=float, default=0.05)
    r.add_argument('--models', required=True)
    r.add_argument('--p360', default=None)
    r.add_argument('--debug', default=None)
    a = ap.parse_args(argv)
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass
    if a.cmd == 'fetch':
        fetch(json.loads(a.models))
        return 0
    if a.cmd == 'run':
        run(a)
        return 0
    return 2


if __name__ == '__main__':
    sys.exit(main())
