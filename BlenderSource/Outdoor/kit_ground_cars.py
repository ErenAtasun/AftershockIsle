
def prism_yz(b, pts, x0, x1, mat, mat_cap=None):
    """Extrude a polygon given in (y, z) between x0 and x1."""
    mat_cap = mat if mat_cap is None else mat_cap
    l = [(x0, y, z) for y, z in pts]
    r = [(x1, y, z) for y, z in pts]
    made = [b.face(list(reversed(l)), mat_cap), b.face(r, mat_cap)]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        made.append(b.face([l[i], l[j], r[j], r[i]], mat))
    bmesh.ops.recalc_face_normals(b.bm, faces=[m for m in made if m])


# =====================================================================  GROUND
def ground_tile(name, variant, seed):
    b = Builder(name, seed)
    r = b.rng
    W, D = 6.0, 12.0
    # slab (top at z=0). Top is split into strips so small height variation reads as damage.
    b.bx(-W / 2, W / 2, -D / 2, D / 2, -0.35, 0.0, ['asphalt', 'asphalt', 'concrete', 'concrete', 'concrete', 'concrete'], faces=(1, 1, 1, 1, 1, 1))
    # dashed centre line (yellow, worn)
    for i in range(4):
        if variant == 'C' and i == 2:
            continue
        x0 = -W / 2 + 0.2 + i * 1.5
        b.bx(x0, x0 + 0.9, -0.07, 0.07, 0.0, 0.007, P['hazard'], faces=(0, 1, 0, 0, 0, 0))
    # edge lines
    b.bx(-W / 2, W / 2, -D / 2 + 1.0, -D / 2 + 1.07, 0.0, 0.006, P['paint'], faces=(0, 1, 0, 0, 0, 0))
    b.bx(-W / 2, W / 2, D / 2 - 1.07, D / 2 - 1.0, 0.0, 0.006, P['paint'], faces=(0, 1, 0, 0, 0, 0))
    if variant == 'A':
        # worn crosswalk stripes
        for i in range(5):
            y0 = -2.0 + i * 0.9
            if r.random() < 0.25:
                continue
            b.bx(1.2, 2.2, y0, y0 + 0.5, 0.0, 0.007, P['paint'], faces=(0, 1, 0, 0, 0, 0))
    if variant == 'B':
        # manhole + pothole + patched strip
        b.cyl((-1.2, 1.5, 0.004), 0.42, 0.012, P['steel_dk'], seg=14)
        b.cyl((-1.2, 1.5, 0.012), 0.3, 0.008, P['rust_dk'], seg=14)
        px, py = 1.4, -2.2
        b.bx(px - 0.5, px + 0.5, py - 0.4, py + 0.4, -0.03, 0.0, P['g6'], faces=(0, 1, 0, 0, 0, 0))
        b.bx(px - 0.55, px + 0.55, py - 0.45, py + 0.45, -0.005, 0.004, P['g5'], faces=(0, 1, 0, 0, 0, 0))
        b.bx(-2.4, -0.2, -3.6, -2.9, 0.0, 0.01, P['g4'], faces=(0, 1, 1, 1, 1, 1))
    if variant == 'C':
        # spilled rubble and glass shards on the road
        for _ in range(14):
            x, y = r.uniform(-2.6, 2.6), r.uniform(-3.5, 3.5)
            s = r.uniform(0.08, 0.28)
            c = r.choice([P['g3'], P['g2'], P['stone'], P['concrete_lt'], P['g4']])
            b.box((x, y, s * 0.35), (s * 1.4, s, s * 0.7), c, rot=(0, 0, r.uniform(0, 3.1)), taper=0.3)
        for _ in range(8):
            x, y = r.uniform(-2.8, 2.8), r.uniform(-3.8, 3.8)
            b.bx(x, x + 0.1, y, y + 0.06, 0.0, 0.008, P['glass_hi'], faces=(0, 1, 0, 0, 0, 0))
    # roadside gutter grate on the far edge
    b.bx(1.0, 1.7, D / 2 - 0.55, D / 2 - 0.15, 0.0, 0.01, P['steel_dk'], faces=(0, 1, 1, 1, 1, 1))
    for k in range(4):
        b.bx(1.05 + k * 0.16, 1.1 + k * 0.16, D / 2 - 0.5, D / 2 - 0.2, 0.01, 0.014, P['black'], faces=(0, 1, 0, 0, 0, 0))
    return b.build()


def curb_front(name, seed):
    """Raised sidewalk with curb along the camera-side of the street. Mass sits toward -Y (front)."""
    b = Builder(name, seed)
    r = b.rng
    L = 6.0
    # curb stone (street side edge at y=0)
    b.bx(-L / 2, L / 2, -0.22, 0.0, -0.3, 0.2, ['concrete', 'concrete', 'concrete', 'concrete', 'concrete', 'concrete'])
    # sidewalk slab
    b.bx(-L / 2, L / 2, -3.6, -0.22, -0.3, 0.18, ['sidewalk', 'sidewalk', 'concrete', 'concrete', 'concrete', 'concrete'])
    # broken edge chips
    for _ in range(5):
        x = r.uniform(-L / 2 + 0.3, L / 2 - 0.3)
        b.box((x, -0.11, 0.21), (r.uniform(0.15, 0.4), 0.2, 0.05), P['g3'], rot=(0, 0, r.uniform(-0.3, 0.3)))
    # planter / debris on the sidewalk
    b.bx(-1.9, -1.2, -2.8, -2.1, 0.18, 0.5, P['concrete_lt'], faces=(0, 1, 1, 1, 1, 1))
    b.bx(-1.8, -1.3, -2.7, -2.2, 0.5, 0.56, P['dirt_dk'], faces=(0, 1, 0, 0, 0, 0))
    for i in range(3):
        b.box((-1.55 + (i - 1) * 0.12, -2.45, 0.7 + i * 0.05), (0.04, 0.04, 0.3), P['leaf_dk'], rot=(0.2 * (i - 1), 0.15, 0))
    # litter and leaves
    for _ in range(9):
        x, y = r.uniform(-L / 2 + 0.2, L / 2 - 0.2), r.uniform(-3.3, -0.5)
        s = r.uniform(0.07, 0.2)
        b.box((x, y, 0.18 + s * 0.3), (s * 1.3, s, s * 0.6), r.choice([P['g3'], P['paint'], P['dirt'], P['canvas'], P['tarp_blue']]),
              rot=(0, 0, r.uniform(0, 3)), taper=0.25)
    # storm drain
    b.bx(1.6, 2.3, -0.2, 0.0, 0.0, 0.021, P['black'], faces=(0, 1, 1, 0, 1, 1))
    return b.build()


# =====================================================================  CARS
def wheel(b, x, y, r=0.32, w=0.24, flat=False, hub=P['steel']):
    z = r if not flat else r * 0.78
    b.cyl((x, y, z), r, w, P['tire'], seg=12, axis='Y')
    sign = 1 if y > 0 else -1
    b.cyl((x, y + sign * (w / 2 + 0.005), z), r * 0.55, 0.02, hub, seg=10, axis='Y')
    b.cyl((x, y + sign * (w / 2 + 0.02), z), r * 0.2, 0.02, P['rust_dk'], seg=6, axis='Y')


def arches(b, xs, y_out, r, z):
    for x in xs:
        for sgn in (-1, 1):
            b.cyl((x, sgn * y_out, z), r, 0.012, P['black'], seg=14, axis='Y')


def sedan(name, color, seed, wreck=0.5, burnt=False):
    b = Builder(name, seed)
    r = b.rng
    paint = P['black'] if burnt else color
    paint2 = P['g6'] if burnt else color
    body = [(-1.8, 0.38), (1.8, 0.38), (1.8, 0.7), (1.62, 0.9), (0.55, 0.94), (-1.6, 0.94), (-1.8, 0.72)]
    b.prism_xz(body, -0.85, 0.85, paint)
    # darker underside / bumpers
    b.bx(-1.84, -1.74, -0.82, 0.82, 0.36, 0.58, P['g5'])
    b.bx(1.74, 1.84, -0.82, 0.82, 0.36, 0.58, P['g5'])
    arches(b, (-1.15, 1.15), 0.853, 0.4, 0.38)
    cab = [(-1.0, 0.94), (-0.72, 1.42), (0.42, 1.42), (0.98, 0.94)]
    b.prism_xz(cab, -0.74, 0.74, paint2)
    # side windows
    for sgn in (-1, 1):
        y0, y1 = (0.74, 0.755) if sgn > 0 else (-0.755, -0.74)
        glass = P['glass_dk']
        b.prism_xz([(-0.88, 0.98), (-0.66, 1.37), (-0.06, 1.37), (-0.06, 0.98)], y0, y1, glass)
        b.prism_xz([(0.04, 0.98), (0.04, 1.37), (0.4, 1.37), (0.84, 0.98)], y0, y1, glass)
    # windshield / rear glass (slanted)
    b.quad((0.43, -0.64, 1.38), (0.43, 0.64, 1.38), (0.93, 0.64, 0.97), (0.93, -0.64, 0.97), P['glass_dk'])
    b.quad((-0.71, 0.64, 1.38), (-0.71, -0.64, 1.38), (-0.94, -0.64, 0.97), (-0.94, 0.64, 0.97), P['glass_dk'])
    # lights
    for sgn in (-1, 1):
        b.bx(1.78, 1.84, sgn * 0.5 - 0.17, sgn * 0.5 + 0.17, 0.62, 0.74, P['headlight'])
        b.bx(-1.84, -1.78, sgn * 0.5 - 0.17, sgn * 0.5 + 0.17, 0.62, 0.74, P['taillight'])
    # wheels
    flat_idx = r.randrange(4)
    k = 0
    for x in (-1.15, 1.15):
        for y in (-0.80, 0.80):
            wheel(b, x, y, flat=(k == flat_idx and wreck > 0.3))
            k += 1
    # wreck damage
    if wreck > 0.2:
        # bent hood
        b.box((1.15, 0.0, 1.02), (0.9, 1.6, 0.08), paint, rot=(0, -0.22, 0), taper=0.0)
        # dent on roof
        b.bx(-0.3, 0.2, -0.3, 0.3, 1.42, 1.455, P['g5'])
        # door ajar
        sgn = r.choice((-1, 1))
        b.box((-0.1, sgn * 0.93, 0.72), (0.9, 0.06, 0.55), paint, rot=(0, 0, sgn * 0.22))
        # scrapes
        for _ in range(4):
            x = r.uniform(-1.4, 1.4)
            b.bx(x, x + r.uniform(0.2, 0.5), 0.855, 0.862, 0.45 + r.uniform(0, 0.3), 0.5 + r.uniform(0.0, 0.4), P['rust_dk'])
    if burnt:
        for _ in range(8):
            x = r.uniform(-1.5, 1.5)
            b.bx(x, x + 0.5, -0.865, -0.858, 0.4, 0.9, P['black'])
    else:
        for _ in range(5):
            x = r.uniform(-1.6, 1.4)
            sgn = r.choice((-1, 1))
            y0 = 0.852 if sgn > 0 else -0.862
            b.bx(x, x + r.uniform(0.1, 0.3), y0, y0 + 0.008, 0.45 + r.uniform(0, 0.2), 0.5 + r.uniform(0.05, 0.22), P['rust_dk'])
    return b.build()


def van(name, color, seed):
    b = Builder(name, seed)
    r = b.rng
    body = [(-2.3, 0.42), (2.3, 0.42), (2.3, 1.0), (1.95, 1.18), (1.35, 1.72), (1.2, 2.05), (-2.3, 2.05)]
    b.prism_xz(body, -0.95, 0.95, color)
    # roof panel
    b.bx(-2.28, 1.1, -0.9, 0.9, 2.05, 2.1, P['g1'])
    # bumpers
    b.bx(-2.34, -2.24, -0.9, 0.9, 0.38, 0.62, P['g5'])
    b.bx(2.24, 2.34, -0.9, 0.9, 0.38, 0.62, P['g5'])
    arches(b, (-1.45, 1.55), 0.953, 0.46, 0.42)
    # cab glass
    for sgn in (-1, 1):
        y0, y1 = (0.95, 0.965) if sgn > 0 else (-0.965, -0.95)
        b.prism_xz([(1.3, 1.25), (1.18, 1.95), (1.74, 1.45), (1.9, 1.25)], y0, y1, P['glass_dk'])
        # sliding door seam + handle
        b.bx(-0.3, -0.27, y0, y1, 0.45, 1.9, P['g6'])
        b.bx(-0.55, -0.35, y0, y1 + sgn * 0.01, 1.1, 1.14, P['steel'])
    b.quad((1.75, -0.85, 1.42), (1.75, 0.85, 1.42), (1.28, 0.85, 1.95), (1.28, -0.85, 1.95), P['glass_dk'])
    # rear doors
    b.bx(-2.31, -2.29, -0.02, 0.02, 0.4, 1.95, P['g6'])
    b.bx(-2.32, -2.29, -0.8, -0.5, 1.0, 1.5, P['rust_dk'])
    # lights
    for sgn in (-1, 1):
        b.bx(2.28, 2.34, sgn * 0.62 - 0.17, sgn * 0.62 + 0.17, 0.7, 0.85, P['headlight'])
        b.bx(-2.34, -2.28, sgn * 0.8 - 0.06, sgn * 0.8 + 0.06, 0.9, 1.4, P['taillight'])
    k = 0
    flat_idx = r.randrange(4)
    for x in (-1.45, 1.55):
        for y in (-0.98, 0.98):
            wheel(b, x, y, r=0.36, w=0.26, flat=(k == flat_idx))
            k += 1
    # roof rack, rust, graffiti band
    for x in (-1.6, -0.6, 0.4):
        b.bx(x, x + 0.06, -0.8, 0.8, 2.1, 2.16, P['steel_dk'])
    b.bx(-2.28, 1.0, -0.8, 0.8, 2.16, 2.2, P['steel_dk'], faces=(0, 0, 1, 1, 1, 1))
    for _ in range(6):
        x = r.uniform(-2.1, 0.9)
        sgn = r.choice((-1, 1))
        y0 = 0.955 if sgn > 0 else -0.965
        b.bx(x, x + r.uniform(0.15, 0.45), y0, y0 + 0.008, 0.55 + r.uniform(0, 0.6), 0.65 + r.uniform(0.1, 0.5), P['rust_dk'])
    b.bx(-2.0, 0.6, 0.955, 0.965, 0.75, 0.9, P['hazard'])
    return b.build()


def pickup(name, color, seed):
    b = Builder(name, seed)
    r = b.rng
    # chassis + cab
    b.prism_xz([(-2.2, 0.45), (2.2, 0.45), (2.2, 0.95), (1.8, 1.05), (0.3, 1.05), (-2.2, 1.05)], -0.9, 0.9, color)
    cab = [(0.15, 1.05), (0.4, 1.6), (1.1, 1.62), (1.7, 1.05)]
    b.prism_xz(cab, -0.82, 0.82, color)
    for sgn in (-1, 1):
        y0, y1 = (0.82, 0.835) if sgn > 0 else (-0.835, -0.82)
        b.prism_xz([(0.3, 1.1), (0.48, 1.52), (1.05, 1.52), (1.5, 1.1)], y0, y1, P['glass_dk'])
    b.quad((1.12, -0.7, 1.58), (1.12, 0.7, 1.58), (1.68, 0.7, 1.08), (1.68, -0.7, 1.08), P['glass_dk'])
    # bed walls
    b.bx(-2.2, 0.1, -0.9, -0.82, 1.05, 1.5, color)
    b.bx(-2.2, 0.1, 0.82, 0.9, 1.05, 1.5, color)
    b.bx(-2.2, -2.12, -0.9, 0.9, 1.05, 1.5, color)
    b.bx(0.0, 0.1, -0.9, 0.9, 1.05, 1.5, color)
    b.bx(-2.12, 0.0, -0.82, 0.82, 1.0, 1.04, P['wood_dk'])
    # cargo: tarp lump + crates
    b.box((-1.1, 0.0, 1.3), (1.5, 1.3, 0.55), P['tarp_green'], taper=0.25)
    b.bx(-0.45, 0.0, -0.4, 0.2, 1.04, 1.5, P['wood'])
    for sgn in (-1, 1):
        b.bx(2.14, 2.2, sgn * 0.6 - 0.15, sgn * 0.6 + 0.15, 0.7, 0.82, P['headlight'])
        b.bx(-2.26, -2.2, sgn * 0.7 - 0.08, sgn * 0.7 + 0.08, 0.7, 1.0, P['taillight'])
    b.bx(2.2, 2.3, -0.8, 0.8, 0.4, 0.62, P['g5'])
    arches(b, (-1.4, 1.35), 0.903, 0.44, 0.44)
    k = 0
    flat_idx = r.randrange(4)
    for x in (-1.4, 1.35):
        for y in (-0.93, 0.93):
            wheel(b, x, y, r=0.36, w=0.26, flat=(k == flat_idx))
            k += 1
    for _ in range(6):
        x = r.uniform(-2.0, 1.9)
        sgn = r.choice((-1, 1))
        y0 = 0.895 if sgn > 0 else -0.905
        b.bx(x, x + r.uniform(0.15, 0.4), y0, y0 + 0.008, 0.6 + r.uniform(0, 0.3), 0.7 + r.uniform(0.05, 0.25), P['rust_dk'])
    return b.build()


# =====================================================================  BARRIERS / COVER
def jersey(name, seed, broken=False):
    b = Builder(name, seed)
    r = b.rng
    prof = [(-0.32, 0.0), (0.32, 0.0), (0.32, 0.22), (0.16, 0.7), (0.1, 1.0), (-0.1, 1.0), (-0.16, 0.7), (-0.32, 0.22)]
    prism_yz(b, prof, -1.1, 1.1, 'concrete', 'concrete')
    # reflective + hazard markings
    for sgn in (-1, 1):
        y = sgn * 0.175
        b.bx(-0.95, 0.95, y - 0.003 if sgn < 0 else y + 0.0, y + 0.003 if sgn > 0 else y, 0.5, 0.56, P['orange'])
    b.bx(-1.1, -0.95, -0.15, 0.15, 0.995, 1.012, P['hazard'], faces=(0, 1, 0, 0, 0, 0))
    b.bx(0.95, 1.1, -0.15, 0.15, 0.995, 1.012, P['hazard'], faces=(0, 1, 0, 0, 0, 0))
    for _ in range(4):
        x = r.uniform(-1.0, 1.0)
        b.box((x, r.choice((-0.14, 0.14)), 0.9), (0.18, 0.1, 0.1), P['g3'], rot=(0, 0, r.uniform(0, 1)))
    if broken:
        # chunk missing at one end with rebar
        b.bx(0.65, 1.1, -0.34, 0.34, 0.45, 1.05, P['g4'], faces=(0, 0, 0, 0, 0, 0))
        for i in range(4):
            y = -0.15 + i * 0.1
            b.box((1.0, y, 0.85), (0.5, 0.018, 0.018), P['rust'], rot=(0, r.uniform(-0.3, 0.2), 0))
        b.box((0.85, 0, 0.2), (0.5, 0.5, 0.3), P['g3'], taper=0.3, rot=(0, 0, 0.4))
    return b.build()


def sandbags(name, seed):
    b = Builder(name, seed)
    r = b.rng
    cols = [P['canvas'], P['canvas_dk'], P['cloth_cream'], P['g6'] if False else P['canvas']]
    z = 0.0
    for row in range(3):
        off = 0.0 if row % 2 == 0 else 0.22
        n = 5 if row < 2 else 4
        x = -1.0 + off + (0 if row < 2 else 0.1)
        for i in range(n):
            w = 0.44
            c = r.choice([P['canvas'], P['canvas_dk'], P['cloth_cream'], P['canvas']])
            b.box((x + w / 2, r.uniform(-0.03, 0.03), z + 0.15), (w * 0.96, 0.52, 0.3), c, taper=0.22,
                  rot=(0, 0, r.uniform(-0.05, 0.05)))
            x += w
        z += 0.27
    # torn sack with sand spill
    b.box((0.85, 0.2, 0.05), (0.5, 0.5, 0.1), P['sand'], taper=0.5)
    b.box((-1.05, -0.1, 0.06), (0.3, 0.3, 0.12), P['sand'], taper=0.5)
    # wood stakes
    b.bx(-1.1, -1.04, -0.05, 0.05, 0.0, 0.95, P['wood_dk'])
    b.bx(1.04, 1.1, -0.05, 0.05, 0.0, 0.95, P['wood_dk'])
    return b.build()


def dumpster(name, color, seed):
    b = Builder(name, seed)
    r = b.rng
    # tapered bin body
    b.box((0, 0, 0.7), (2.0, 1.0, 1.0), color, taper=0.0)
    b.bx(-1.04, 1.04, -0.54, 0.54, 1.18, 1.22, P['steel_dk'], faces=(0, 1, 1, 1, 1, 1))
    # lid (open, tilted)
    b.box((0.0, -0.1, 1.5), (1.9, 0.9, 0.06), P['g5'], rot=(0.65, 0, 0))
    # contents: tarp lumps, bags
    for _ in range(7):
        x, y = r.uniform(-0.8, 0.8), r.uniform(-0.3, 0.3)
        s = r.uniform(0.25, 0.5)
        b.box((x, y, 1.25 + s * 0.3), (s, s * 0.9, s * 0.7), r.choice([P['tarp_blue'], P['g6'], P['canvas'], P['tarp_green'], P['dirt_dk']]),
              taper=0.3, rot=(0, 0, r.uniform(0, 3)))
    # ribs + wheels
    for x in (-0.6, 0.0, 0.6):
        b.bx(x - 0.03, x + 0.03, -0.51, 0.51, 0.25, 1.15, P['steel_dk'], faces=(0, 0, 1, 1, 0, 0))
    for sgn in (-1, 1):
        b.cyl((sgn * 0.8, -0.38, 0.14), 0.14, 0.08, P['tire'], seg=8, axis='Y')
        b.cyl((sgn * 0.8, 0.38, 0.14), 0.14, 0.08, P['tire'], seg=8, axis='Y')
    for _ in range(6):
        x = r.uniform(-0.9, 0.9)
        b.bx(x, x + r.uniform(0.15, 0.35), -0.512, -0.505, 0.3 + r.uniform(0, 0.4), 0.4 + r.uniform(0.1, 0.3), P['rust_dk'])
    return b.build()


def tire_pile(name, seed):
    b = Builder(name, seed)
    r = b.rng
    for i in range(3):
        for j in range(2):
            x, y = (i - 1) * 0.62 + r.uniform(-0.03, 0.03), (j - 0.5) * 0.62
            b.cyl((x, y, 0.1), 0.3, 0.2, P['tire'], seg=10)
            b.cyl((x, y, 0.21), 0.16, 0.02, P['g6'], seg=8)
    b.cyl((0.0, 0.0, 0.3), 0.3, 0.2, P['tire'], seg=10)
    b.cyl((0.0, 0.0, 0.41), 0.16, 0.02, P['g6'], seg=8)
    b.cyl((-0.6, 0.35, 0.3), 0.3, 0.2, P['tire'], seg=10, rot=(1.1, 0, 0.3))
    return b.build()


def cones(name, seed):
    b = Builder(name, seed)
    r = b.rng
    for i in range(3):
        x, y = (i - 1) * 0.9, r.uniform(-0.2, 0.2)
        b.bx(x - 0.2, x + 0.2, y - 0.2, y + 0.2, 0.0, 0.04, P['black'])
        rot = (0, 0, 0) if i != 1 else (math.pi / 2 * 0.95, 0, 0.5)
        cz = 0.33 if i != 1 else 0.2
        b.cyl((x, y, cz), 0.16, 0.6, P['orange'], seg=8, r_top=0.03, rot=rot)
        if i != 1:
            b.cyl((x, y, 0.38), 0.115, 0.1, P['white'], seg=8, r_top=0.095)
    return b.build()
