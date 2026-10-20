# README görselleri: dış mekan kitinden sokak manzarası (oyun kamerası açısı) ve model seçkisi.
# Arka planda çalıştır:  blender -b --factory-startup -P BlenderSource/Outdoor/render_readme.py
import bpy, os, sys, math, random
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "Docs", "images")
os.makedirs(OUT, exist_ok=True)

g = globals()
for f in ("kit_core.py", "kit_ground_cars.py", "kit_buildings.py", "kit_props.py", "kit_export.py"):
    exec(open(os.path.join(HERE, f), encoding="utf-8").read(), g)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

items = build_everything()
by = {o.name: o for o in items}
cat = {o.name: MANIFEST[o.name] for o in items}
for o in items:
    o.location = (0, -500, 0)
    o.hide_render = True

scene = bpy.context.scene
coll = bpy.data.collections.new("Render")
scene.collection.children.link(coll)
rng = random.Random(11)


def put(name, x, y, z=0.0, yaw=0.0):
    src = by[name]
    ob = src.copy()
    ob.data = src.data
    ob.location = (x, y, z)
    ob.rotation_euler = (0, 0, math.radians(yaw))
    ob.hide_render = False
    coll.objects.link(ob)
    return ob


def names(category, prefix=None):
    return [n for n, m in cat.items() if m["category"] == category and (prefix is None or n.startswith(prefix))]


def setup_render(engine_hint="EEVEE"):
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    for candidate in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        if candidate in engines:
            try:
                scene.render.engine = candidate
                break
            except TypeError:
                continue
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    try:
        scene.view_settings.view_transform = 'AgX'
    except TypeError:
        pass
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = next((n for n in world.node_tree.nodes if n.type == 'BACKGROUND'), None)
    if bg:
        bg.inputs[0].default_value = (0.42, 0.47, 0.54, 1)
        bg.inputs[1].default_value = 0.9


def add_sun(rot, energy=3.2):
    data = bpy.data.lights.new("Sun", 'SUN')
    data.energy = energy
    try:
        data.angle = math.radians(4)
    except AttributeError:
        pass
    sun = bpy.data.objects.new("Sun", data)
    coll.objects.link(sun)
    sun.rotation_euler = rot
    return sun


def camera(name, location, rotation, fov_deg=28):
    data = bpy.data.cameras.new(name)
    data.sensor_fit = 'VERTICAL'
    data.angle_y = math.radians(fov_deg)
    cam = bpy.data.objects.new(name, data)
    coll.objects.link(cam)
    cam.location = location
    cam.rotation_euler = rotation
    scene.camera = cam
    return cam


def render(path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


# ---------------------------------------------------------------- 1) Sokak: oyun kamerası açısı
originX, length, minZ, maxZ = 0.0, 40.0, 0.0, 9.0
MaxX = originX + length
x = originX - 14
while x < MaxX + 14:
    put(rng.choice(names("ground")), x + 3, minZ + 4.5, 0, rng.choice((0, 180)))
    put("Curb_Front", x + 3, minZ - 0.5, 0)
    x += 6
for strip in range(3):
    z = maxZ + 1.5 + 7 + 14 * strip
    x = originX - 20
    while x < MaxX + 20:
        put(rng.choice(names("groundback")), x + 3, z, -0.02, 0 if (strip + int(x)) % 2 == 0 else 180)
        x += 6


def row(left, right, frontz, gap, tall):
    cur, last = left, None
    while cur < right:
        n = rng.choice(names("building"))
        for _ in range(6):
            if n != last and not (tall and by[n].dimensions.z < 12):
                break
            n = rng.choice(names("building"))
        w = max(6, by[n].dimensions.x)
        put(n, cur + w / 2, frontz + by[n].get("facade", 3.0), -0.02)
        cur += w + rng.uniform(*gap)
        last = n


row(originX - 16, MaxX + 16, maxZ + 3.6, (0.2, 1.6), False)
row(originX - 20, MaxX + 20, maxZ + 16, (0.5, 4), True)
xx = originX - 28
while xx < MaxX + 28:
    put(rng.choice(names("skyline")), xx + 18, maxZ + 32, -0.02)
    xx += 34

xx = originX - 12
while xx < MaxX + 12:
    xx += rng.uniform(5, 10)
    r = rng.random()
    pre = "Prop_Lamp" if r < .3 else "Prop_Tree" if r < .46 else "Prop_Hydrant" if r < .56 else "Prop_Bench" if r < .66 else "Prop_BusShelter" if r < .72 else "Prop_Sign" if r < .84 else "Prop_TrashBags"
    cands = [n for n in cat if n.startswith(pre)]
    # Kameranın tam önüne uzun direk gelmesin (kadrajı bölüyordu).
    if pre in ("Prop_Lamp", "Prop_TrafficLight", "Prop_Tree") and 12 < xx < 28:
        continue
    if cands:
        n = rng.choice(cands)
        ob = put(n, xx, minZ - 0.5 - rng.uniform(1.2, 2.4), 0.18, rng.choice((0, 180)))
        if n.startswith("Prop_Tree"):
            ob.scale = (0.75, 0.75, 0.75)
        xx += by[n].dimensions.x * 0.5

xx = originX - 12
while xx < MaxX + 12:
    xx += rng.uniform(2, 6)
    r = rng.random()
    z = maxZ + rng.uniform(1.0, 2.0)
    if r < .3:
        for i in range(rng.randint(2, 4)):
            put(rng.choice(["Prop_Fence", "Prop_Fence_Intact"]), xx + i * 3, maxZ + 1.1)
        xx += 9
    elif r < .52:
        n = rng.choice(["Prop_Rubble_A", "Prop_Rubble_B", "Prop_Rubble_Big"])
        put(n, xx, z + .4, 0, rng.uniform(0, 360))
        xx += by[n].dimensions.x * .6
    elif r < .6:
        put("Prop_PowerPole", xx, maxZ + 1.4)
    elif r < .72:
        put(rng.choice(names("debris")), xx, z, 0, rng.uniform(0, 360))
    elif r < .8:
        put(rng.choice(["Prop_Container", "Prop_Container_Rust"]), xx + 3, maxZ + 2.1, 0, rng.choice((0, 180)))
        xx += 6
    elif r < .88:
        put(rng.choice(names("dumpster")), xx, maxZ + 1.5, 0, rng.choice((0, 180)))
    else:
        put(rng.choice(names("barrier")), xx, z, 0, rng.uniform(-20, 20))

for n, ox, oy, yaw in [("Car_Sedan_Blue", 9, 2.2, 0), ("Car_Van", 17, 6.6, 180), ("Barrier_Jersey", 27, 1.6, 0),
                       ("Sandbags", 31, 7.0, 0), ("Dumpster", 12.5, 7.8, 0), ("Car_Pickup", 35, 3.5, 90),
                       ("Prop_Rubble_B", 22, 2.4, 0), ("Car_Sedan_Burnt", 4, 6.8, 180)]:
    put(n, ox, oy, 0, yaw)

setup_render()
add_sun((math.radians(52), 0, math.radians(28)))
pitch = math.radians(20)
focus = Vector((20, 4.5, 0.8))
forward = Vector((0, math.cos(pitch), -math.sin(pitch)))
camera("GameCam", focus - forward * 21, (math.radians(90 - 20), 0, 0))
render(os.path.join(OUT, "outdoor_street.png"))

# ---------------------------------------------------------------- 2) Model seçkisi
for o in list(coll.objects):
    bpy.data.objects.remove(o, do_unlink=True)

front = ["Car_Sedan_Blue", "Car_Van", "Car_Pickup", "Car_Sedan_Burnt"]
back = ["Dumpster", "Barrier_Jersey", "Sandbags", "Prop_Lamp", "Prop_TrafficLight", "Prop_BusShelter", "Prop_Rubble_Big", "Prop_Container_Rust"]
for i in range(-5, 5):
    put("Ground_A" if i % 2 else "Ground_C", i * 6 + 3, 3, 0, 0)


def lay(row_names, y, gap):
    total = sum(by[n].dimensions.x for n in row_names) + gap * (len(row_names) - 1)
    cur = -total / 2
    for n in row_names:
        w = by[n].dimensions.x
        put(n, cur + w / 2, y, 0, 0)
        cur += w + gap


lay(front, 0.6, 1.0)
lay(back, 5.0, 0.7)
for i, n in enumerate(["Bld_Shop_Brick", "Bld_Apartment_A", "Bld_Collapsed_Brick", "Bld_Row_Brick"]):
    put(n, (i - 1.5) * 13.5, 15 + by[n].get("facade", 3.0), -0.02)
for i in range(-4, 4):
    put("Ground_Back_A", i * 6 + 3, 15, -0.02)

add_sun((math.radians(48), 0, math.radians(-30)), 3.0)
camera("KitCam", (0, -16, 9.5), (math.radians(63), 0, 0), 40)
render(os.path.join(OUT, "outdoor_kit.png"))
print("README renders written to", OUT)
