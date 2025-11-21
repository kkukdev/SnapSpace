import numpy as np
import torch
import copy
import datetime
import os
import sys
import glob
import argparse
import trimesh
import json
import open3d as o3d
from util.objmesh import ObjMesh
from util.models import Dataset, Mesh
from util.networks import Net
import util.loss as Loss

from torch.utils.tensorboard import SummaryWriter
from torch_geometric.data import Data
from torch_geometric.utils import to_undirected

parser = argparse.ArgumentParser(description='Deep mesh prior for denoising')
parser.add_argument('-i', '--input', type=str, required=True)
parser.add_argument('--lr', type=float, default=0.01)
parser.add_argument('--iter', type=int, default=1000)
parser.add_argument('--skip', type=bool, default=False)
parser.add_argument('--lap', type=float, default=1.4)
parser.add_argument('--save-every', type=int, default=50, help='Epoch interval for saving intermediate .obj outputs.')
parser.add_argument('--no-log', action='store_true', help='Disable TensorBoard logging to reduce I/O.')
parser.add_argument('--log-dir', type=str, default='./logs/denoise', help='Root directory for TensorBoard logs.')
FLAGS = parser.parse_args()

for k, v in vars(FLAGS).items():
    print('{:10s}: {}'.format(k, v))

device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')

file_path = FLAGS.input
def _collect_mesh_files(folder):
    candidates = []
    for pattern in ("*.obj", "*.glb", "*.gltf"):
        candidates.extend(glob.glob(os.path.join(folder, pattern)))
    return sorted(set(candidates))

def _load_mesh_any(path):
    ext = os.path.splitext(path)[1].lower()

    if ext in (".obj",):
        # OBJ는 Open3D로 바로 로드
        m = o3d.io.read_triangle_mesh(path)
        m.compute_vertex_normals()
        # Open3D TriangleMesh에는 triangulate()가 없다 → 절대 호출 금지
        vs = np.asarray(m.vertices, dtype=np.float32)
        fs = np.asarray(m.triangles, dtype=np.int32)

    elif ext in (".glb", ".gltf"):
        # GLB/GLTF는 trimesh로 로드 후 '삼각형화' 보장
        tm = trimesh.load(path, force="mesh", process=False)
        if not tm.is_watertight or tm.faces.shape[1] != 3:
            trimesh.repair.triangulate_faces(tm)  # 다각형 → 삼각형
        vs = np.asarray(tm.vertices, dtype=np.float32)
        fs = np.asarray(tm.faces, dtype=np.int32)

    else:
        raise ValueError(f"Unsupported mesh extension: {ext}")

    return vs, fs

def _pick_files(p):
    if os.path.isdir(p):
        meshes = _collect_mesh_files(p)
        if not meshes:
            raise FileNotFoundError(f"No supported mesh files (*.obj, *.glb, *.gltf) under: {p}")
        noisy = [f for f in meshes if "noisy" in os.path.basename(f).lower()]
        if len(meshes) >= 2 and noisy:
            input_file = noisy[0]
            label_file = next((f for f in meshes if f != input_file), None)
        else:
            input_file, label_file = meshes[0], None
        mesh_name = os.path.basename(os.path.normpath(p))
    else:
        if not os.path.isfile(p):
            raise FileNotFoundError(p)
        ext = os.path.splitext(p)[1].lower()
        if ext not in ('.obj', '.glb', '.gltf'):
            raise ValueError(f"Unsupported mesh extension: {ext}")
        input_file = p
        label_file = None
        mesh_name = os.path.splitext(os.path.basename(p))[0]
    return input_file, label_file, mesh_name

input_file, label_file, mesh_name = _pick_files(file_path)

# GT 경로 구성 및 선택적 로드
gt_file = os.path.join("datasets", "groundtruth", f"{mesh_name}.obj")
g_mesh = Mesh(gt_file) if os.path.exists(gt_file) else None
USE_GT = g_mesh is not None


# 입력/라벨 로드
i_mesh = Mesh(input_file)
l_mesh = Mesh(label_file) if label_file else None
if l_mesh is None:
    print("[warn] label mesh not found → fallback to input mesh")
    l_mesh = i_mesh


# node-features and edge-index
np.random.seed(42)
torch.manual_seed(42)
if torch.cuda.is_available():
    torch.cuda.manual_seed_all(42)
n_verts = l_mesh.vs.shape[0]  # 위 폴백으로 None 아님
x = torch.randn((n_verts, 16), dtype=torch.float32, device=device, requires_grad=True)
x_pos = torch.tensor(i_mesh.vs, dtype=torch.float32, device=device)
y = torch.tensor(l_mesh.vs, dtype=torch.float32, device=device)
edge_index = torch.from_numpy(l_mesh.edges.T.copy()).long()
edge_index = to_undirected(edge_index, num_nodes=n_verts)
init_mad = Loss.mad(l_mesh, g_mesh) if (USE_GT and l_mesh is not None) else None

data = Data(x=x, y=y, x_pos=x_pos, edge_index=edge_index)
dataset = Dataset(data)
dataset.x = dataset.x.to(device)
dataset.y = dataset.y.to(device)
dataset.edge_index = dataset.edge_index.to(device)

# create model instance
model = Net(FLAGS.skip).to(device)
model.train()

# output experimental conditions
dt_now = datetime.datetime.now().strftime('%Y-%m-%d-%H-%M-%S')
run_suffix = mesh_name + dt_now
out_dir = os.path.join("./datasets/d_output", run_suffix)
os.makedirs(out_dir, exist_ok=True)
writer = None
if FLAGS.no_log:
    print("[info] tensorboard logging disabled (--no-log).")
else:
    # Windows 경로 문제 해결을 위해 절대 경로 사용
    log_dir = os.path.abspath(os.path.join(FLAGS.log_dir, run_suffix))
    os.makedirs(log_dir, exist_ok=True)
    
    # TensorBoard SummaryWriter를 try-catch로 감싸서 에러 처리
    try:
        writer = SummaryWriter(log_dir=log_dir)
        print(f"[info] TensorBoard logging enabled: {log_dir}")
    except Exception as e:
        print(f"[error] Failed to initialize TensorBoard writer: {e}")
        print("[info] Disabling TensorBoard logging...")
        writer = None
log_file = out_dir + "/condition.json"
condition = {"input":input_file, "label":label_file, "gt": gt_file, "iter": FLAGS.iter ,"lap": FLAGS.lap, "skip": FLAGS.skip, "init_mad": init_mad, "lr": FLAGS.lr}

with open(log_file, mode="w") as f:
    l = json.dumps(condition, indent=2)
    f.write(l)

# learning loop
min_mad = 1000
print("initial_mad_value: ", init_mad)

optimizer = torch.optim.Adam(model.parameters(), lr=FLAGS.lr)

base_mesh = ObjMesh.from_data(i_mesh.faces, i_mesh.vs)
base_mesh.vs = base_mesh.vertices
base_mesh.faces = i_mesh.faces
save_every = max(1, FLAGS.save_every)
o_mesh = None

for epoch in range(1, FLAGS.iter+1):
    optimizer.zero_grad()
    out = model(dataset)
    loss1 = Loss.mse_loss(out, dataset.y)
    loss2 = Loss.mesh_laplacian_loss(out, l_mesh)
    loss = loss1 + FLAGS.lap * loss2
    loss.backward()
    optimizer.step()
    if writer is not None:
        writer.add_scalar("total_loss", loss, epoch)
        writer.add_scalar("mse_loss", loss1, epoch)
        writer.add_scalar("laplacian_loss", loss2, epoch)
    if epoch % 10 == 0:
        print('Epoch %d | Loss: %.4f' % (epoch, loss.item()))
    if epoch % save_every == 0 or epoch == FLAGS.iter:
        o_mesh = copy.deepcopy(base_mesh)
        verts = out.to('cpu').detach().numpy().copy()
        o_mesh.vertices = verts
        o_mesh.vs = verts
        o_mesh.save(os.path.join(out_dir, f'{epoch}_output.obj'))

        # ↓↓↓ 추가: GT 있을 때만 MAD 계산
        if USE_GT:
            mad_value = Loss.mad(o_mesh, g_mesh)
            min_mad = min(mad_value, min_mad)
            print("mad_value: ", mad_value, "min_mad: ", min_mad)
            if writer is not None:
                writer.add_scalar("mean_angle_difference", mad_value, epoch)

if USE_GT and o_mesh is not None and l_mesh is not None:
    final_mad = Loss.mad(o_mesh, g_mesh)
    print(f"mad(before→after): {init_mad:.4f} → {final_mad:.4f}")
else:
    print("GT 없음 또는 라벨 없음 → mad 평가는 생략")

if writer is not None:
    writer.close()
