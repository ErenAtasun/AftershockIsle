
# =====================================================================  STREET FURNITURE / DEBRIS (decor, no gameplay colliders)

def street_lamp(name, seed, broken=False):
    b = Builder(name, seed)
    r = b.rng
    b.cyl((0, 0, 0.12), 0.22, 0.24, P['g4'], seg=10, r_top=0.15)
    if not broken:
        b.cyl((0, 0, 2.7), 0.075, 5.2, P['steel_dk'], seg=8, r_top=0.055)
        b.box((0.55, 0, 5.25), (1.3, 0.09, 0.09), P['steel_dk'], rot=(0, 0.06, 0))
        b.bx(0.9, 1.5, -0.17, 0.17, 5.12, 5.28, P['g5'])
        b.bx(1.0, 1.4, -0.12, 0.12, 5.06, 5.12, P['headlight'])
    else:
        # pole snapped and leaning; lamp head hangs on its cable
        b.cyl((0, 0, 1.6), 0.075, 3.2, P['steel_dk'], seg=8, r_top=0.065)
        b.cyl((0.45, 0, 3.55), 0.07, 1.3, P['steel_dk'], seg=8, r_top=0.055, rot=(0, 0.55, 0))
        b.box((1.05, 0, 3.95), (0.7, 0.09, 0.09), P['steel_dk'], rot=(0, -0.2, 0))
        b.box((1.5, 0, 3.55), (0.55, 0.3, 0.14), P['g5'], rot=(0, 0.9, 0))
        b.box((1.35, 0, 3.85), (0.015, 0.015, 0.55), P['black'], rot=(0, 0.35, 0))
        b.box((0.0, 0.1, 0.5), (0.15, 0.25, 0.4), P['rust_dk'], rot=(0.3, 0, 0))
    return b.build()


def traffic_light(name, seed):
    b = Builder(name, seed)
    b.cyl((0, 0, 0.1), 0.2, 0.2, P['g4'], seg=8)
    b.cyl((0, 0, 2.2), 0.07, 4.4, P['steel_dk'], seg=8)
    b.box((0.6, 0, 4.4), (1.3, 0.08, 0.08), P['steel_dk'], rot=(0, 0.1, 0))
    head = (1.2, 0, 4.15)
    b.box(head, (0.34, 0.3, 1.05), P['g6'], rot=(0.08, 0, 0.1))
    for k, col in enumerate((P['red'], P['hazard'], P['sign_green'])):
        b.cyl((1.2 + 0.0, -0.17, 4.45 - k * 0.32), 0.1, 0.04, col if k == 1 else P['g5'], seg=8, axis='Y')
    b.box((-0.9, 0.0, 3.0), (0.5, 0.06, 0.6), P['sign_blue'], rot=(0, 0, 0.0))
    return b.build()


def road_sign(name, seed, kind='stop'):
    b = Builder(name, seed)
    b.cyl((0, 0, 1.2), 0.04, 2.4, P['steel'], seg=6)
    if kind == 'stop':
        b.cyl((0, -0.05, 2.3), 0.4, 0.04, P['red'], seg=8, axis='Y', rot=(0, 0, 0))
        b.bx(-0.3, 0.3, -0.075, -0.06, 2.25, 2.35, P['white'])
    elif kind == 'warn':
        b.prism_xz([(-0.45, 2.0), (0.45, 2.0), (0, 2.8)], -0.07, -0.03, P['hazard'])
        b.bx(-0.03, 0.03, -0.075, -0.07, 2.2, 2.55, P['black'])
    else:
        b.bx(-0.7, 0.7, -0.07, -0.03, 2.1, 2.7, P['sign_green'])
        b.bx(-0.55, 0.55, -0.075, -0.07, 2.35, 2.42, P['white'])
        b.bx(-0.4, 0.4, -0.075, -0.07, 2.2, 2.27, P['white'])
    # bend the whole sign a bit
    for v in b.bm.verts:
        v.co.x += v.co.z * 0.08
    return b.build()


def power_pole(name, seed):
    b = Builder(name, seed)
    r = b.rng
    b.cyl((0, 0, 3.8), 0.16, 7.6, P['wood_dk'], seg=8, r_top=0.11)
    b.bx(-1.1, 1.1, -0.06, 0.06, 7.0, 7.12, P['wood'])
    b.bx(-0.8, 0.8, -0.06, 0.06, 6.2, 6.3, P['wood'])
    for x in (-1.0, -0.4, 0.4, 1.0):
        b.cyl((x, 0, 7.2), 0.04, 0.14, P['g1'], seg=6)
    for x in (-0.7, 0.7):
        b.cyl((x, 0, 6.4), 0.04, 0.14, P['g1'], seg=6)
    # transformer can
    b.cyl((0.35, -0.2, 5.5), 0.22, 0.6, P['steel_dk'], seg=8)
    # drooping wires (stubs) to both sides
    for x0, dirn in ((-1.0, -1), (1.0, 1)):
        for k in range(4):
            xs = x0 + dirn * k * 0.6
            b.box((xs + dirn * 0.3, 0, 7.12 - k * 0.12 - 0.02 * k * k), (0.62, 0.02, 0.02), P['black'], rot=(0, -dirn * (0.12 + k * 0.06), 0))
    b.box((0.0, 0, 3.6), (0.02, 0.02, 7.2), P['g6'], rot=(0, 0, 0))
    for v in b.bm.verts:
        v.co.x += v.co.z * 0.05
    return b.build()


def dead_tree(name, seed, height=5.5):
    b = Builder(name, seed)
    r = b.rng

    def branch(base, direction, length, radius, depth):
        d = Vector(direction).normalized()
        end = Vector(base) + d * length
        mid = (Vector(base) + end) / 2
        # orient cylinder along d
        up = Vector((0, 0, 1))
        axis = up.cross(d)
        angle = math.acos(max(-1, min(1, up.dot(d))))
        rot = Matrix.Rotation(angle, 3, axis).to_euler() if axis.length > 1e-5 else (0, 0, 0)
        b.cyl(mid, radius, length, P['wood_dk'] if depth < 2 else P['wood_char'], seg=6, r_top=radius * 0.62, rot=rot)
        if depth < 3:
            n = 2 if depth < 2 else 1 + r.randint(0, 1)
            for _ in range(n):
                nd = Vector((d.x + r.uniform(-0.8, 0.8), d.y * 0.3 + r.uniform(-0.25, 0.25), d.z * 0.6 + r.uniform(0.2, 0.7)))
                branch(end, nd, length * r.uniform(0.55, 0.75), radius * 0.62, depth + 1)
        elif r.random() < 0.55:
            b.box(end, (r.uniform(0.3, 0.6), r.uniform(0.3, 0.5), r.uniform(0.2, 0.4)), P['leaf_dk'] if r.random() < 0.7 else P['dirt_dk'],
                  taper=0.3, rot=(0, 0, r.uniform(0, 3)))

    b.cyl((0, 0, height * 0.2), 0.3, height * 0.4, P['wood_dk'], seg=7, r_top=0.2)
    branch((0, 0, height * 0.38), (0.15, 0, 1), height * 0.35, 0.2, 1)
    branch((0, 0, height * 0.3), (-0.8, 0.1, 0.9), height * 0.28, 0.14, 1)
    branch((0, 0, height * 0.34), (0.9, 0.1, 0.8), height * 0.28, 0.14, 1)
    b.box((0.0, 0.0, 0.05), (0.8, 0.8, 0.1), P['dirt_dk'], taper=0.5)
    return b.build()


def bench(name, seed):
    b = Builder(name, seed)
    r = b.rng
    for x in (-0.8, 0.8):
        b.bx(x - 0.04, x + 0.04, -0.28, 0.28, 0.0, 0.42, P['steel_dk'])
        b.bx(x - 0.04, x + 0.04, 0.22, 0.28, 0.42, 0.95, P['steel_dk'])
    for k in range(4):
        if k == 2:
            b.box((0.2, -0.2 + k * 0.12, 0.2), (0.9, 0.1, 0.04), P['wood_dk'], rot=(0, 0.25, 0.1))
            continue
        b.bx(-0.95, 0.95, -0.27 + k * 0.14, -0.27 + k * 0.14 + 0.11, 0.42, 0.46, P['wood'])
    for k in range(3):
        b.bx(-0.95, 0.95, 0.22, 0.26, 0.55 + k * 0.14, 0.64 + k * 0.14, P['wood'] if k != 1 else P['wood_dk'])
    return b.build()


def bus_shelter(name, seed):
    b = Builder(name, seed)
    r = b.rng
    for x in (-1.5, 1.5):
        b.bx(x - 0.05, x + 0.05, -0.6, -0.5, 0.0, 2.5, P['steel_dk'])
        b.bx(x - 0.05, x + 0.05, 0.5, 0.6, 0.0, 2.5, P['steel_dk'])
    b.bx(-1.65, 1.65, -0.7, 0.7, 2.5, 2.58, P['steel'])
    b.bx(-1.5, 1.5, 0.5, 0.56, 0.35, 2.4, P['glass'], faces=(0, 0, 1, 1, 0, 0))
    # shattered side panel (frame only) and ad box
    b.bx(-1.5, -0.2, 0.56, 0.62, 0.35, 2.4, P['glass_hi'], faces=(0, 0, 1, 1, 0, 0))
    b.bx(0.1, 1.2, 0.46, 0.52, 0.7, 2.1, P['paint'])
    b.bx(0.12, 1.18, 0.455, 0.46, 0.75, 2.05, P['sign_blue'])
    b.bx(-1.3, 0.8, -0.5, -0.1, 0.42, 0.5, P['wood_dk'])
    b.bx(-0.7, -0.5, -0.5, -0.1, 0.0, 0.42, P['steel_dk'])
    b.bx(1.0, 1.2, -0.5, -0.1, 0.0, 0.2, P['g3'])
    for _ in range(5):
        x = r.uniform(-1.4, 1.4)
        b.box((x, r.uniform(-0.4, 0.4), 0.04), (0.12, 0.1, 0.05), r.choice([P['glass_hi'], P['paint'], P['g3']]), rot=(0, 0, r.uniform(0, 3)))
    return b.build()


def fence(name, seed, gap=True):
    b = Builder(name, seed)
    r = b.rng
    L = 3.0
    h = 1.9
    for x in (-L / 2, L / 2):
        b.cyl((x, 0, h / 2), 0.04, h, P['steel'], seg=6)
    b.bx(-L / 2, L / 2, -0.025, 0.025, h - 0.04, h, P['steel'])
    b.bx(-L / 2, L / 2, -0.025, 0.025, 0.0, 0.04, P['steel'])
    for k in range(13):
        x = -L / 2 + 0.1 + k * (L - 0.2) / 12
        if gap and 5 <= k <= 8:
            b.bx(x - 0.008, x + 0.008, -0.006, 0.006, 1.1, h - 0.04, P['steel_dk'])
            continue
        b.bx(x - 0.008, x + 0.008, -0.006, 0.006, 0.04, h - 0.04, P['steel_dk'])
    for k in range(10):
        z = 0.15 + k * 0.17
        if gap and z < 1.1:
            b.bx(-L / 2, -0.25, -0.006, 0.006, z, z + 0.015, P['steel_dk'])
            b.bx(0.8, L / 2, -0.006, 0.006, z, z + 0.015, P['steel_dk'])
        else:
            b.bx(-L / 2, L / 2, -0.006, 0.006, z, z + 0.015, P['steel_dk'])
    if gap:
        b.box((0.0, 0.0, 0.4), (0.9, 0.02, 0.3), P['steel_dk'], rot=(0, 0.25, 0.1))
    # caught litter
    for _ in range(4):
        b.box((r.uniform(-1.2, 1.2), 0.05, r.uniform(0.2, 1.2)), (0.15, 0.03, 0.12), r.choice([P['paint'], P['tarp_blue'], P['g3']]), rot=(0, r.uniform(-0.5, 0.5), 0))
    return b.build()


def hydrant(name, seed):
    b = Builder(name, seed)
    b.cyl((0, 0, 0.35), 0.15, 0.7, P['red'], seg=8)
    b.cyl((0, 0, 0.78), 0.17, 0.08, P['red'], seg=8)
    b.cyl((0, 0, 0.85), 0.1, 0.1, P['red'], seg=8, r_top=0.06)
    b.cyl((0.2, 0, 0.45), 0.06, 0.15, P['red'], seg=6, axis='X')
    b.cyl((-0.2, 0, 0.45), 0.06, 0.15, P['red'], seg=6, axis='X')
    b.cyl((0, 0, 0.05), 0.19, 0.1, P['g4'], seg=8)
    return b.build()


def trash_bags(name, seed):
    b = Builder(name, seed)
    r = b.rng
    for _ in range(9):
        x, y = r.uniform(-0.5, 0.5), r.uniform(-0.35, 0.35)
        s = r.uniform(0.3, 0.55)
        z = s * 0.35 + (0.25 if r.random() < 0.3 and abs(x) < 0.3 else 0.0)
        b.box((x, y, z), (s, s * 0.85, s * 0.7), r.choice([P['black'], P['g6'], P['g5'], P['tarp_blue'], P['tarp_green']]),
              taper=r.uniform(0.2, 0.5), rot=(r.uniform(-0.2, 0.2), r.uniform(-0.2, 0.2), r.uniform(0, 3)))
    for _ in range(6):
        b.box((r.uniform(-0.9, 0.9), r.uniform(-0.6, 0.6), 0.02), (0.12, 0.09, 0.03), r.choice([P['paint'], P['canvas'], P['dirt']]), rot=(0, 0, r.uniform(0, 3)))
    return b.build()


def pallet_stack(name, seed):
    b = Builder(name, seed)
    r = b.rng
    for lvl in range(3):
        z = lvl * 0.16
        for k in range(3):
            b.bx(-0.55, 0.55, -0.55 + k * 0.5 - 0.04, -0.55 + k * 0.5 + 0.04, z, z + 0.08, P['wood_gr'] if lvl % 2 else P['wood'])
        for k in range(4):
            x = -0.5 + k * 0.33
            b.bx(x - 0.05, x + 0.05, -0.55, 0.55, z + 0.08, z + 0.16, P['wood_dk'])
    b.box((0.3, 0.2, 0.62), (0.9, 0.7, 0.04), P['wood'], rot=(0.15, 0.1, 0.4))
    return b.build()


def rubble_heap(name, seed, w=3.6, d=2.6, h=1.3, count=34):
    b = Builder(name, seed)
    r = b.rng
    cols = [P['g3'], P['g2'], P['stone'], P['concrete_lt'], P['g4'], P['ash'], P['wood_gr'], P['rust_dk']]
    for _ in range(count):
        t = r.random() ** 0.8
        ang = r.uniform(0, math.tau)
        rad = (1.0 - t)
        x, y = math.cos(ang) * w / 2 * rad, math.sin(ang) * d / 2 * rad
        s = r.uniform(0.25, 0.8) * (0.6 + 0.4 * (1 - t) )
        z = h * (1.0 - rad) * 0.9 + s * 0.2
        c = r.choice(cols)
        b.box((x, y, z + s * 0.2), (s * r.uniform(0.8, 1.5), s * r.uniform(0.7, 1.2), s * r.uniform(0.4, 0.85)), c,
              rot=(r.uniform(-0.45, 0.45), r.uniform(-0.45, 0.45), r.uniform(0, 3.1)), taper=r.uniform(0.0, 0.35))
    for _ in range(max(3, count // 7)):
        x, y = r.uniform(-w * 0.25, w * 0.25), r.uniform(-d * 0.25, d * 0.25)
        b.box((x, y, h * 0.8 + r.uniform(0, 0.3)), (0.025, 0.025, r.uniform(0.6, 1.4)), P['rust'], rot=(r.uniform(-0.6, 0.6), r.uniform(-0.6, 0.6), 0))
    for _ in range(3):
        b.box((r.uniform(-w * 0.3, w * 0.3), r.uniform(-d * 0.3, d * 0.3), h * 0.5), (r.uniform(0.9, 1.6), 0.1, 0.06), P['wood'],
              rot=(r.uniform(-0.3, 0.3), r.uniform(-0.3, 0.3), r.uniform(0, 3.1)))
    return b.build()


def slab_lean(name, seed):
    b = Builder(name, seed)
    r = b.rng
    b.box((0.0, 0.0, 0.85), (2.8, 0.26, 1.9), 'concrete', rot=(-0.52, 0, 0.04))
    b.box((0.0, 0.62, 0.15), (2.8, 1.2, 0.3), P['g3'], rot=(0.05, 0, 0.0), taper=0.1)
    for k in range(5):
        x = -1.1 + k * 0.55
        b.box((x, -0.55, 1.65), (0.025, 0.025, 0.7), P['rust'], rot=(-0.52 + r.uniform(-0.2, 0.2), 0, r.uniform(-0.2, 0.2)))
    for _ in range(8):
        b.box((r.uniform(-1.3, 1.3), r.uniform(0.2, 1.0), r.uniform(0.1, 0.35)), (r.uniform(0.3, 0.7), r.uniform(0.3, 0.6), r.uniform(0.2, 0.4)),
              r.choice([P['g3'], P['g2'], P['stone']]), rot=(0, 0, r.uniform(0, 3)), taper=0.2)
    return b.build()


def container(name, color, seed):
    b = Builder(name, seed)
    r = b.rng
    L, W, H = 6.0, 2.4, 2.6
    b.bx(-L / 2, L / 2, -W / 2, W / 2, 0.15, H, color)
    for k in range(24):
        x = -L / 2 + 0.15 + k * (L - 0.3) / 23
        b.bx(x - 0.03, x + 0.03, -W / 2 - 0.05, -W / 2, 0.25, H - 0.1, P['steel_dk'])
        b.bx(x - 0.03, x + 0.03, W / 2, W / 2 + 0.05, 0.25, H - 0.1, P['steel_dk'])
    for sx in (-1, 1):
        b.bx(sx * (L / 2) - 0.06, sx * (L / 2) + 0.06, -W / 2, W / 2, 0.0, H + 0.05, P['steel_dk'])
    b.bx(-L / 2, L / 2, -W / 2 - 0.02, W / 2 + 0.02, 0.0, 0.18, P['steel_dk'])
    for _ in range(8):
        x = r.uniform(-L / 2 + 0.3, L / 2 - 0.9)
        b.bx(x, x + r.uniform(0.3, 1.2), -W / 2 - 0.055, -W / 2 - 0.05, 0.4 + r.uniform(0, 1.4), 0.7 + r.uniform(0.2, 1.4), P['rust'])
    b.bx(-1.0, 1.0, -W / 2 - 0.058, -W / 2 - 0.054, 1.0, 1.5, P['white'])
    return b.build()


def ground_back(name, seed, variant='A'):
    """Open ground behind the walkable street: cracked sidewalk slab + dirt + rubble. 6 x 14, top at z=0."""
    b = Builder(name, seed)
    r = b.rng
    W, D = 6.0, 14.0
    b.bx(-W / 2, W / 2, -D / 2, D / 2, -0.35, 0.0, ['sidewalk', 'sidewalk', 'concrete', 'concrete', 'concrete', 'concrete'])
    for _ in range(18):
        x, y = r.uniform(-W / 2 + 0.4, W / 2 - 0.4), r.uniform(-D / 2 + 0.4, D / 2 - 0.4)
        sx, sy = r.uniform(0.6, 1.6), r.uniform(0.6, 1.6)
        b.box((x, y, 0.015), (sx, sy, 0.03), r.choice([P['dirt_dk'], P['dirt'], P['mud'], P['ash']]), rot=(0, 0, r.uniform(0, 3)), taper=0.0)
    for _ in range(24 if variant == 'A' else 12):
        x, y = r.uniform(-W / 2 + 0.3, W / 2 - 0.3), r.uniform(-D / 2 + 0.3, D / 2 - 0.3)
        s = r.uniform(0.15, 0.55)
        b.box((x, y, s * 0.3), (s * 1.4, s, s * 0.6), r.choice([P['g3'], P['stone'], P['g4'], P['ash']]), rot=(0, 0, r.uniform(0, 3.1)), taper=0.3)
    for _ in range(10):
        x, y = r.uniform(-W / 2 + 0.2, W / 2 - 0.2), r.uniform(-D / 2 + 0.2, D / 2 - 0.2)
        b.box((x, y, 0.1), (0.5, 0.04, 0.04), P['wood_dk'], rot=(0, 0, r.uniform(0, 3.1)))
    return b.build()
