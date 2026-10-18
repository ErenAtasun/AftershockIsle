
# =====================================================================  BUILDINGS (backdrop)
# Front facade faces -Y. Back side is a plain wall. Balconies/awnings stick out of the front (zbias = -1).

def rubble_pile(b, x0, x1, y0, y1, height, count, seed=1, towards=1):
    """Chunky pile of concrete/brick between x0..x1; rises toward y1 (the wall)."""
    r = random.Random(seed)
    cols = [P['g3'], P['g2'], P['stone'], P['concrete_lt'], P['g4'], P['rust_dk'], P['ash'], P['wood_gr']]
    for _ in range(count):
        x = r.uniform(x0, x1)
        t = r.random()
        y = y1 + (y0 - y1) * (t ** 0.8)
        hmax = height * (1.0 - t) ** 0.9
        s = r.uniform(0.25, 0.9)
        z = r.uniform(0.0, max(0.05, hmax))
        c = r.choice(cols)
        b.box((x, y, z + s * 0.3), (s * r.uniform(0.8, 1.6), s * r.uniform(0.7, 1.3), s * r.uniform(0.4, 0.9)), c,
              rot=(r.uniform(-0.4, 0.4), r.uniform(-0.4, 0.4), r.uniform(0, 3.1)), taper=r.uniform(0.0, 0.3))
    # rebar tufts
    for _ in range(max(2, count // 8)):
        x = r.uniform(x0, x1)
        y = r.uniform(y1 + (y0 - y1) * 0.5, y1)
        b.box((x, y, height * 0.55 + r.uniform(0, 0.4)), (0.02, 0.02, r.uniform(0.6, 1.4)), P['rust'],
              rot=(r.uniform(-0.5, 0.5), r.uniform(-0.5, 0.5), 0))


def building(name, wall, W, D, floors, bays, seed, fh=3.1, missing=None, balconies=(), shop=False, strip=False,
             soot=False, awning=False, roof_clutter=True, parapet=True, broken_roof=False, rubble=None, sawtooth=False,
             shutters=False, curtain=0.35):
    b = Builder(name, seed)
    r = b.rng
    t = 0.38
    bw = W / bays
    yF = -D / 2
    H = floors * fh
    if missing is None:
        missing = lambda f, i: False
    glass = P['glass_dk']

    # plinth
    b.bx(-W / 2 - 0.2, W / 2 + 0.2, yF - 0.25, D / 2 + 0.1, 0.0, 0.3, 'concrete')

    # side + rear walls (with jagged top when broken_roof)
    for sx in (-1, 1):
        x0, x1 = (sx * W / 2 - (0.32 if sx > 0 else 0), sx * W / 2 + (0 if sx > 0 else 0.32))
        top = H
        if broken_roof:
            top = H - r.uniform(0.0, 2.2)
        b.bx(min(x0, x1), max(x0, x1), yF, D / 2, 0.3, top, wall)
    rear_top = H if not broken_roof else H - r.uniform(0.0, 1.5)
    b.bx(-W / 2, W / 2, D / 2 - 0.35, D / 2, 0.3, rear_top, [wall, wall, 'plaster_grey', wall, wall, wall])

    # floor slabs and ceiling
    for f in range(floors + 1):
        z = f * fh + 0.3 if f > 0 else 0.3
        if f == floors and broken_roof:
            continue
        thick = 0.28
        # slab missing under collapsed cells only at the facade edge is not modelled; slab stays
        b.bx(-W / 2 + 0.3, W / 2 - 0.3, yF + 0.05, D / 2 - 0.3, z - 0.02, z + thick, ['concrete', 'concrete_lt'] if False else 'concrete')

    # interior columns and stuff, visible through missing cells
    for f in range(floors):
        z0 = f * fh + 0.58
        for i in range(bays):
            if not missing(f, i):
                continue
            xm = -W / 2 + (i + 0.5) * bw
            # interior back wall paint is already in the rear wall; add furniture lumps and hanging junk
            for _ in range(2):
                b.box((xm + r.uniform(-0.8, 0.8), r.uniform(yF + 1.0, D / 2 - 1.0), z0 + 0.35),
                      (r.uniform(0.5, 1.1), r.uniform(0.4, 0.9), r.uniform(0.5, 0.9)),
                      r.choice([P['wood_dk'], P['wood'], P['cloth_cream'], P['g4'], P['tarp_blue']]), rot=(0, 0, r.uniform(0, 3)), taper=0.1)
            # ragged slab lip at the open edge
            b.box((xm, yF + 0.1, f * fh + 0.3 + 0.12), (bw * 0.95, 0.5, 0.18), P['g3'], rot=(r.uniform(-0.1, 0.1), 0, r.uniform(-0.05, 0.05)))
            if f + 1 < floors and not missing(f + 1, i):
                # sagging overhead slab edge with rebar
                for k in range(4):
                    b.box((xm - bw * 0.35 + k * bw * 0.23, yF + 0.2, (f + 1) * fh + 0.18), (0.02, 0.02, r.uniform(0.5, 1.0)), P['rust'],
                          rot=(r.uniform(-0.7, 0.2), 0, r.uniform(-0.2, 0.2)))

    # facade
    for f in range(floors):
        z0 = f * fh + 0.3
        z1 = z0 + fh
        if f == floors - 1:
            z1 = H
        for i in range(bays):
            x0 = -W / 2 + i * bw
            x1 = x0 + bw
            xm = (x0 + x1) / 2
            if missing(f, i):
                continue
            is_shop = shop and f == 0
            ww = (bw - 0.5) if is_shop else min(1.6, bw - 0.9)
            sill = 0.25 if is_shop else 0.95
            head = 2.6 if is_shop else 2.35
            if strip:
                ww = bw - 0.15
                sill = 0.95
                head = 2.5
            ww = max(0.9, ww)
            is_door = (f == 0 and not shop and not strip and bays >= 3 and i == bays // 2)
            if is_door:
                ww, sill, head = 1.35, 0.0, 2.35
            xl, xr = xm - ww / 2, xm + ww / 2
            b.bx(x0, xl, yF, yF + t, z0, z1, wall)
            b.bx(xr, x1, yF, yF + t, z0, z1, wall)
            if sill > 0.01:
                b.bx(xl, xr, yF, yF + t, z0, z0 + sill, wall)
            b.bx(xl, xr, yF, yF + t, z0 + head, z1, wall)
            if is_door:
                # entrance: dark opening, worn steps, door leaf hanging open
                b.bx(xl, xr, yF + t - 0.05, yF + t - 0.02, z0, z0 + head, P['black'], faces=(0, 0, 1, 0, 0, 0))
                b.bx(xl - 0.25, xr + 0.25, yF - 0.8, yF, 0.0, z0 + 0.06, 'concrete')
                b.bx(xl - 0.12, xr + 0.12, yF - 0.45, yF - 0.8, 0.0, z0 - 0.08, 'concrete') if False else None
                b.box((xr - 0.1, yF - 0.38, z0 + 1.1), (0.95, 0.05, 2.15), P['wood_dk'], rot=(0, 0, 1.15))
                b.bx(xl - 0.12, xl, yF - 0.06, yF + 0.05, z0, z0 + head + 0.1, 'concrete')
                b.bx(xr, xr + 0.12, yF - 0.06, yF + 0.05, z0, z0 + head + 0.1, 'concrete')
                continue
            # glass / recess
            has_glass = not soot and r.random() > 0.35
            b.bx(xl, xr, yF + t - 0.03, yF + t - 0.01, z0 + sill, z0 + head, glass if has_glass else P['black'], faces=(0, 0, 1, 0, 0, 0))
            # frame sill
            b.bx(xl - 0.1, xr + 0.1, yF - 0.14, yF + 0.05, z0 + sill - 0.07, z0 + sill, 'concrete')
            # lintel shadow band
            b.bx(xl - 0.05, xr + 0.05, yF - 0.03, yF, z0 + head, z0 + head + 0.12, P['g4'])
            # broken pane shards / curtain
            roll = r.random()
            if roll < curtain:
                cc = r.choice([P['cloth_orange'], P['cloth_cream'], P['tarp_blue'], P['canvas']])
                b.bx(xl + 0.05, xr - 0.05, yF + t - 0.08, yF + t - 0.05, z0 + head - r.uniform(0.5, 1.1), z0 + head - 0.05, cc, faces=(0, 0, 1, 0, 0, 0))
            elif roll < curtain + 0.15:
                # plywood board-up
                b.bx(xl + 0.02, xr - 0.02, yF + 0.05, yF + 0.12, z0 + sill + 0.05, z0 + head - 0.05, P['ply'])
                b.bx(xl, xr, yF + 0.04, yF + 0.13, z0 + sill + 0.5, z0 + sill + 0.58, P['wood_dk'])
            if soot:
                b.bx(xl - 0.12, xr + 0.12, yF - 0.02, yF, z0 + head, z0 + head + r.uniform(0.9, 2.0), P['black'], faces=(0, 0, 1, 0, 0, 0))
            if shutters and is_shop:
                drop = r.uniform(0.9, 2.2)
                b.bx(xl, xr, yF - 0.03, yF + 0.02, z0 + head - drop, z0 + head, P['steel_dk'])
                for k in range(int(drop / 0.12)):
                    b.bx(xl, xr, yF - 0.05, yF - 0.03, z0 + head - drop + k * 0.12, z0 + head - drop + k * 0.12 + 0.03, P['steel'])
            if awning and is_shop:
                col = r.choice([P['tarp_red'], P['tarp_blue'], P['tarp_green'], P['canvas']])
                b.box(((xl + xr) / 2, yF - 0.6, z0 + head + 0.2), (ww + 0.3, 1.3, 0.05), col, rot=(0.38, 0, r.uniform(-0.03, 0.03)))
                b.bx(xl - 0.12, xl - 0.06, yF - 1.2, yF - 1.14, z0 + head - 0.6, z0 + head + 0.5, P['steel_dk'])
        # balconies
        for (bf, bi) in balconies:
            if bf != f or missing(f, bi):
                continue
            xm = -W / 2 + (bi + 0.5) * bw
            bwid = bw * 0.78
            b.bx(xm - bwid / 2, xm + bwid / 2, yF - 1.25, yF + 0.05, z0 - 0.05, z0 + 0.15, 'concrete')
            for sx in (-1, 1):
                b.bx(xm + sx * bwid / 2 - 0.03, xm + sx * bwid / 2 + 0.03, yF - 1.25, yF - 1.19, z0 + 0.15, z0 + 1.05, P['steel_dk'])
            b.bx(xm - bwid / 2, xm + bwid / 2, yF - 1.25, yF - 1.19, z0 + 1.0, z0 + 1.06, P['steel_dk'])
            if r.random() < 0.6:
                for k in range(5):
                    xx = xm - bwid / 2 + 0.2 + k * (bwid - 0.4) / 4
                    if r.random() < 0.8:
                        b.bx(xx - 0.01, xx + 0.01, yF - 1.24, yF - 1.2, z0 + 0.15, z0 + 1.0, P['steel_dk'])
            if r.random() < 0.7:
                b.box((xm + r.uniform(-0.4, 0.4), yF - 0.6, z0 + 0.5), (0.5, 0.5, 0.65), r.choice([P['wood_dk'], P['tarp_blue'], P['cloth_cream'], P['g5']]), taper=0.2)

    # ground floor grime
    b.bx(-W / 2, W / 2, yF - 0.008, yF, 0.3, 1.0, P['g5'], faces=(0, 0, 1, 0, 0, 0))

    # parapet / roof edge
    if parapet and not broken_roof:
        for (x0, x1) in ((-W / 2, W / 2),):
            b.bx(x0, x1, yF - 0.05, yF + 0.2, H, H + 0.45, 'concrete')
        for sx in (-1, 1):
            b.bx(sx * W / 2 - 0.1, sx * W / 2 + 0.1, yF, D / 2, H, H + 0.45, 'concrete')
        b.bx(-W / 2, W / 2, D / 2 - 0.2, D / 2, H, H + 0.45, 'concrete')
        # chipped parapet gaps
        for _ in range(2):
            x = r.uniform(-W / 2 + 1, W / 2 - 1)
            b.bx(x, x + r.uniform(0.6, 1.4), yF - 0.06, yF + 0.22, H + 0.1, H + 0.5, P['g5'])
    if sawtooth:
        n = max(3, int(W / 4))
        for i in range(n):
            x0 = -W / 2 + i * W / n
            b.prism_xz([(x0, H), (x0 + W / n, H), (x0 + W / n, H + 1.6)], yF, D / 2, P['steel_dk'], P['steel'])
            b.bx(x0 + 0.05, x0 + 0.05 + W / n * 0.8, yF - 0.01, yF + 0.01, H + 0.1, H + 1.3, P['glass'])
    if roof_clutter and not broken_roof:
        for _ in range(r.randint(2, 4)):
            x = r.uniform(-W / 2 + 1.2, W / 2 - 1.2)
            y = r.uniform(yF + 1.0, D / 2 - 1.0)
            kind = r.choice(['ac', 'tank', 'chimney', 'antenna'])
            if kind == 'ac':
                b.bx(x - 0.7, x + 0.7, y - 0.5, y + 0.5, H + 0.05, H + 0.75, P['g2'])
                b.cyl((x, y - 0.5, H + 0.4), 0.28, 0.06, P['g6'], seg=10, axis='Y')
            elif kind == 'tank':
                b.cyl((x, y, H + 0.9), 0.7, 1.5, P['rust_dk'], seg=10)
                for sx in (-1, 1):
                    b.bx(x + sx * 0.5 - 0.04, x + sx * 0.5 + 0.04, y - 0.04, y + 0.04, H, H + 0.2, P['steel_dk'])
            elif kind == 'chimney':
                b.bx(x - 0.35, x + 0.35, y - 0.35, y + 0.35, H, H + 1.5, 'brick')
            else:
                b.bx(x - 0.02, x + 0.02, y - 0.02, y + 0.02, H, H + r.uniform(2.0, 3.5), P['steel_dk'])
                b.bx(x - 0.5, x + 0.5, y - 0.015, y + 0.015, H + 1.6, H + 1.63, P['steel_dk'])
    if broken_roof:
        # jagged top: stepped chunks + hanging slab
        for _ in range(7):
            x = r.uniform(-W / 2 + 0.5, W / 2 - 0.5)
            b.box((x, r.uniform(yF + 0.5, D / 2 - 0.5), H + r.uniform(-0.2, 0.3)), (r.uniform(0.5, 1.4), r.uniform(0.4, 1.0), r.uniform(0.2, 0.6)), P['g3'],
                  rot=(r.uniform(-0.3, 0.3), r.uniform(-0.3, 0.3), r.uniform(0, 3)))
        for k in range(6):
            x = r.uniform(-W / 2 + 0.6, W / 2 - 0.6)
            b.box((x, r.uniform(yF + 0.5, D / 2 - 0.5), H + 0.5), (0.02, 0.02, r.uniform(0.8, 1.6)), P['rust'], rot=(r.uniform(-0.5, 0.5), r.uniform(-0.5, 0.5), 0))

    # rubble spilling at the base
    if rubble:
        rx0, rx1, rh, rn = rubble
        rubble_pile(b, rx0, rx1, yF - 3.5, yF + 0.2, rh, rn, seed + 7)
    obj = b.build()
    obj["facade"] = D / 2
    return obj


# =====================================================================  SKYLINE (far silhouettes)
def skyline(name, seed, width=36.0, hmin=10.0, hmax=34.0):
    b = Builder(name, seed)
    r = b.rng
    x = -width / 2
    while x < width / 2 - 2:
        w = r.uniform(3.0, 7.0)
        w = min(w, width / 2 - x)
        h = r.uniform(hmin, hmax)
        mat = r.choice(['concrete', 'plaster_grey', 'plaster_beige', 'brick'])
        d = r.uniform(2.0, 4.0)
        b.bx(x, x + w, -d / 2, d / 2, 0.0, h, mat)
        # window grid
        cols = max(1, int(w / 1.6))
        rows = int(h / 3.2)
        for rr in range(rows):
            for cc in range(cols):
                if r.random() < 0.18:
                    continue
                wx = x + (cc + 0.5) * w / cols
                b.bx(wx - 0.4, wx + 0.4, -d / 2 - 0.02, -d / 2, 1.4 + rr * 3.2, 2.5 + rr * 3.2, P['glass_dk'] if r.random() > 0.2 else P['g6'], faces=(0, 0, 1, 0, 0, 0))
        # broken top
        if r.random() < 0.6:
            b.box((x + w * r.uniform(0.2, 0.8), 0, h), (w * 0.4, d * 0.8, r.uniform(0.6, 1.8)), P['g3'], rot=(0, 0, r.uniform(-0.4, 0.4)), taper=0.4)
        x += w + r.uniform(0.0, 0.6)
    return b.build()
