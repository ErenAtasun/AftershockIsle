import bpy, bmesh, math, random, os, json
from mathutils import Vector, Matrix, Euler

PROJ = r"C:\MyUnityGames\AftershockIsle\AftershockIsle"
TEXDIR = PROJ + r"\Assets\Models\Outdoor\Textures"
EXPORT = PROJ + r"\Assets\Models\Outdoor"

MATS = ['pal', 'asphalt', 'sidewalk', 'concrete', 'brick', 'plaster_beige', 'plaster_grey']
TEXFILE = {
    'pal': 'outdoor_palette.png', 'asphalt': 'outdoor_asphalt.png', 'sidewalk': 'outdoor_sidewalk.png',
    'concrete': 'outdoor_concrete.png', 'brick': 'outdoor_brick.png',
    'plaster_beige': 'outdoor_plaster_beige.png', 'plaster_grey': 'outdoor_plaster_grey.png',
}
TILE = {'asphalt': 4.0, 'sidewalk': 2.0, 'concrete': 3.0, 'brick': 2.0, 'plaster_beige': 3.0, 'plaster_grey': 3.0}

# palette cell indices (see make_textures.py)
P = dict(
    g0=0, g1=1, g2=2, g3=3, g4=4, g5=5, g6=6, g7=7,
    rust=8, rust_hi=9, rust_dk=10, grime=11, steel=12, steel_hi=13, steel_dk=14, oldmetal=15,
    wood=16, wood_dk=17, wood_hi=18, wood_blk=19, ply=20, wood2=21, wood_gr=22, wood_char=23,
    car_blue=24, car_red=25, car_white=26, car_green=27, car_yellow=28, car_grey=29, car_dark=30, car_plum=31,
    glass_dk=32, glass=33, glass_hi=34, headlight=35, taillight=36, tire=37, rubber=38, cream=39,
    hazard=40, paint=41, orange=42, sign_green=43, sign_blue=44, red=45, white=46, black=47,
    leaf_dk=48, leaf=49, dirt_dk=50, dirt=51, sand=52, mud=53, ash=54, stone=55,
    canvas=56, canvas_dk=57, tarp_blue=58, tarp_red=59, tarp_green=60, cloth_orange=61, cloth_cream=62, concrete_lt=63,
)


def ensure_materials():
    mats = {}
    for name in MATS:
        mat = bpy.data.materials.get(name)
        if mat is None:
            mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        nt = mat.node_tree
        for n in list(nt.nodes):
            nt.nodes.remove(n)
        out = nt.nodes.new('ShaderNodeOutputMaterial')
        bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
        bsdf.inputs['Roughness'].default_value = 0.92
        tex = nt.nodes.new('ShaderNodeTexImage')
        path = os.path.join(TEXDIR, TEXFILE[name])
        tex.image = bpy.data.images.load(path, check_existing=True)
        tex.interpolation = 'Closest' if name == 'pal' else 'Linear'
        nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
        mats[name] = mat
    return mats


def norm_mat(m):
    if isinstance(m, int):
        return ('pal', m)
    if isinstance(m, str):
        if m in P:
            return ('pal', P[m])
        return (m, 0)
    return m


class Builder:
    def __init__(self, name, seed=1):
        self.name = name
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.rng = random.Random(seed)
        self.uvoff = (self.rng.random(), self.rng.random())
        self.solid_faces = []

    # ------------------------------------------------------------ low level
    def face(self, pts, mat, solid=True):
        name, idx = norm_mat(mat)
        verts = [self.bm.verts.new(Vector(p)) for p in pts]
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = MATS.index(name)
        if name == 'pal':
            u = (idx % 8 + 0.5) / 8.0
            v = 1.0 - (idx // 8 + 0.5) / 8.0
            for loop in f.loops:
                loop[self.uv].uv = (u, v)
        else:
            n = f.normal if f.normal.length > 0 else Vector((0, 0, 1))
            ax = max(range(3), key=lambda i: abs(n[i]))
            t = TILE[name]
            for loop in f.loops:
                p = loop.vert.co
                if ax == 2:
                    uv = (p.x / t, p.y / t)
                elif ax == 1:
                    uv = (p.x / t, p.z / t)
                else:
                    uv = (p.y / t, p.z / t)
                loop[self.uv].uv = (uv[0] + self.uvoff[0], uv[1] + self.uvoff[1])
        if solid:
            self.solid_faces.append(f)
        return f

    def _mats6(self, mat):
        if isinstance(mat, (list,)) and len(mat) == 6:
            return mat
        return [mat] * 6

    # ------------------------------------------------------------ primitives
    def box(self, c, s, mat, rot=(0, 0, 0), taper=0.0, skew=(0.0, 0.0), faces=(1, 1, 1, 1, 1, 1)):
        """Box centered at c with size s. rot: euler radians (XYZ). taper: shrink of top face (0..1).
        skew: shift of top face (x, y). faces: which of [bottom, top, -y, +y, -x, +x] to build."""
        hx, hy, hz = s[0] / 2, s[1] / 2, s[2] / 2
        k = 1.0 - taper
        corners = [
            (-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
            (-hx * k + skew[0], -hy * k + skew[1], hz), (hx * k + skew[0], -hy * k + skew[1], hz),
            (hx * k + skew[0], hy * k + skew[1], hz), (-hx * k + skew[0], hy * k + skew[1], hz),
        ]
        R = Euler(rot).to_matrix()
        cc = Vector(c)
        pts = [cc + R @ Vector(p) for p in corners]
        idxs = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (3, 0, 4, 7), (1, 2, 6, 5)]
        mats = self._mats6(mat)
        made = []
        for i, quad in enumerate(idxs):
            if not faces[i]:
                continue
            f = self.face([pts[j] for j in quad], mats[i])
            if f:
                made.append(f)
        if all(faces):
            bmesh.ops.recalc_face_normals(self.bm, faces=made)
        return pts

    def bx(self, x0, x1, y0, y1, z0, z1, mat, **kw):
        return self.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)), mat, **kw)

    def cyl(self, c, r, h, mat, seg=10, axis='Z', rot=(0, 0, 0), r_top=None, caps=True):
        r_top = r if r_top is None else r_top
        R = Euler(rot).to_matrix()
        if axis == 'X':
            R = R @ Euler((0, math.pi / 2, 0)).to_matrix()
        elif axis == 'Y':
            R = R @ Euler((math.pi / 2, 0, 0)).to_matrix()
        cc = Vector(c)
        bot, top = [], []
        for i in range(seg):
            a = math.tau * i / seg
            bot.append(cc + R @ Vector((math.cos(a) * r, math.sin(a) * r, -h / 2)))
            top.append(cc + R @ Vector((math.cos(a) * r_top, math.sin(a) * r_top, h / 2)))
        made = []
        for i in range(seg):
            j = (i + 1) % seg
            f = self.face([bot[i], bot[j], top[j], top[i]], mat)
            if f:
                made.append(f)
        if caps:
            f = self.face(list(reversed(bot)), mat)
            if f:
                made.append(f)
            if r_top > 0.0001:
                f = self.face(top, mat)
                if f:
                    made.append(f)
        bmesh.ops.recalc_face_normals(self.bm, faces=made)

    def prism_xz(self, pts, y0, y1, mat, mat_cap=None):
        """Extrude a polygon given in (x, z) between y0 and y1 (side profile -> volume)."""
        mat_cap = mat if mat_cap is None else mat_cap
        front = [(x, y0, z) for x, z in pts]
        back = [(x, y1, z) for x, z in pts]
        made = []
        f = self.face(list(reversed(front)), mat_cap)
        made.append(f)
        f = self.face(back, mat_cap)
        made.append(f)
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            f = self.face([front[j], front[i], back[i], back[j]], mat)
            made.append(f)
        bmesh.ops.recalc_face_normals(self.bm, faces=[m for m in made if m])

    def prism_xy(self, pts, z0, z1, mat, mat_cap=None):
        """Extrude a polygon given in (x, y) between z0 and z1 (plan -> volume)."""
        mat_cap = mat if mat_cap is None else mat_cap
        bot = [(x, y, z0) for x, y in pts]
        top = [(x, y, z1) for x, y in pts]
        made = [self.face(list(reversed(bot)), mat_cap), self.face(top, mat_cap)]
        n = len(pts)
        for i in range(n):
            j = (i + 1) % n
            made.append(self.face([bot[i], bot[j], top[j], top[i]], mat))
        bmesh.ops.recalc_face_normals(self.bm, faces=[m for m in made if m])

    def quad(self, p0, p1, p2, p3, mat):
        return self.face([p0, p1, p2, p3], mat)

    def jitter(self, amount, z_min=0.15, seed=3, axes=(1, 1, 1)):
        r = random.Random(seed)
        for v in self.bm.verts:
            if v.co.z < z_min:
                continue
            v.co += Vector((r.uniform(-1, 1) * amount * axes[0], r.uniform(-1, 1) * amount * axes[1], r.uniform(-1, 1) * amount * axes[2]))

    # ------------------------------------------------------------ finish
    def build(self, location=(0, 0, 0)):
        bm = self.bm
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='EAR_CLIP')
        mesh = bpy.data.meshes.new(self.name)
        bm.to_mesh(mesh)
        bm.free()
        for name in MATS:
            mesh.materials.append(bpy.data.materials[name])
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        obj.location = location
        for p in mesh.polygons:
            p.use_smooth = False
        return obj


def clear_assets(prefix=""):
    for obj in list(bpy.data.objects):
        if obj.type == 'MESH' and (not prefix or obj.name.startswith(prefix)):
            bpy.data.objects.remove(obj, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


MANIFEST = {}


def register(obj, category, zbias=0, note=""):
    d = obj.dimensions
    MANIFEST[obj.name] = dict(name=obj.name, category=category, zbias=zbias, size=[round(d.x, 3), round(d.y, 3), round(d.z, 3)],
                              facade=float(obj.get("facade", 0.0)), note=note)


def export_all(objs, folder=EXPORT):
    os.makedirs(folder, exist_ok=True)
    saved = {}
    for obj in objs:
        saved[obj.name] = obj.location.copy()
    for obj in objs:
        bpy.ops.object.select_all(action='DESELECT')
        obj.location = (0, 0, 0)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        path = os.path.join(folder, obj.name + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'MESH'}, global_scale=1.0,
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
            bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False,
            path_mode='STRIP', embed_textures=False, use_tspace=False)
    for obj in objs:
        obj.location = saved[obj.name]
    with open(os.path.join(folder, "outdoor_manifest.json"), "w", encoding="utf-8") as fh:
        json.dump({"items": list(MANIFEST.values())}, fh, indent=1)
    return len(objs)
