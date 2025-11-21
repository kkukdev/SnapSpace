import numpy as np
import torch
from util.models import Mesh

def mae_loss(pred_pos, real_pos, verts_mask=None):
    """mean-absolute error for vertex positions"""
    diff_pos = torch.abs(real_pos - pred_pos)
    diff_pos = torch.sum(diff_pos.squeeze(), dim=1)
    if verts_mask == None:
        mae_pos = torch.sum(diff_pos) / len(diff_pos)
    else:
        mae_pos = torch.sum(diff_pos.T * verts_mask) / (torch.sum(verts_mask) + 1.0e-12)
    return mae_pos

def mse_loss(pred_pos, real_pos, verts_mask=None):
    """mean-square error for vertex positions"""
    diff_pos = torch.abs(real_pos - pred_pos)
    diff_pos = diff_pos ** 2
    diff_pos = torch.sum(diff_pos.squeeze(), dim=1)
    diff_pos = torch.sqrt(diff_pos)
    if verts_mask == None:
        mse_pos = torch.sum(diff_pos) / len(diff_pos)
    else:
        mse_pos = torch.sum(diff_pos * verts_mask) / (torch.sum(verts_mask) + 1.0e-12)
    return mse_pos

def mae_loss_edge_lengths(pred_pos, real_pos, edges):
    """mean-absolute error for edge lengths"""
    pred_edge_pos = pred_pos[edges,:].clone().detach()
    real_edge_pos = real_pos[edges,:].clone().detach()

    pred_edge_lens = torch.abs(pred_edge_pos[:,0,:]-pred_edge_pos[:,1,:])
    real_edge_lens = torch.abs(real_edge_pos[:,0,:]-real_edge_pos[:,1,:])

    pred_edge_lens = torch.sum(pred_edge_lens, dim=1)
    real_edge_lens = torch.sum(real_edge_lens, dim=1)
    
    diff_edge_lens = torch.abs(real_edge_lens - pred_edge_lens)
    mae_edge_lens = torch.mean(diff_edge_lens)

    return mae_edge_lens

def var_edge_lengths(pred_pos, edges):
    """variance of edge lengths"""
    pred_edge_pos = pred_pos[edges,:].clone().detach()

    pred_edge_lens = torch.abs(pred_edge_pos[:,0,:]-pred_edge_pos[:,1,:])

    pred_edge_lens = torch.sum(pred_edge_lens, dim=1)
    
    mean_edge_len = torch.mean(pred_edge_lens, dim=0, keepdim=True)
    var_edge_len = torch.pow(pred_edge_lens - mean_edge_len, 2.0)
    var_edge_len = torch.mean(var_edge_len)

    return var_edge_len

def mesh_laplacian_loss(pred_pos, mesh: Mesh):
    """Simple Laplacian loss that reuses adjacency cached on the mesh."""
    # Expect pred_pos as (N, C) on target device.
    device = pred_pos.device
    rows, cols, degrees = mesh.get_laplacian_cache(device)
    if rows.numel() == 0:
        return torch.tensor(0.0, device=device, dtype=pred_pos.dtype)

    pred = pred_pos
    num_verts, feat_dim = pred.shape

    neighbor_sum = torch.zeros_like(pred)
    scatter_index = rows.unsqueeze(-1).expand(-1, feat_dim)
    neighbor_sum.scatter_add_(0, scatter_index, pred[cols])

    degrees = degrees.to(device=device, dtype=pred.dtype)
    mask = degrees > 0
    safe_degrees = degrees.clone()
    safe_degrees[~mask] = 1.0

    laplacian = pred - neighbor_sum / safe_degrees.unsqueeze(-1)
    masked_laplacian = laplacian[mask]
    lap_vals = torch.sqrt(torch.sum(masked_laplacian * masked_laplacian, dim=1) + 1.0e-12)
    lap_loss = torch.mean(lap_vals) if lap_vals.numel() > 0 else torch.tensor(0.0, device=device, dtype=pred.dtype)

    return lap_loss

def mad(mesh1, mesh2):
    fn1 = Mesh.compute_face_normals(mesh1)
    fn2 = Mesh.compute_face_normals(mesh2)
    inner = [np.inner(fn1[i], fn2[i]) for i in range(fn1.shape[0])]
    sad = np.rad2deg(np.arccos(np.clip(inner, -1.0, 1.0)))
    mad = np.sum(sad) / len(sad)

    return mad
