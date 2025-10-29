import open3d as o3d

m1 = o3d.io.read_triangle_mesh("datasets/d_input/dragon/dragon_noise.obj")
m2 = o3d.io.read_triangle_mesh("1000_output.obj")

print(len(m1.vertices), len(m2.vertices))
print(len(m1.triangles), len(m2.triangles))
