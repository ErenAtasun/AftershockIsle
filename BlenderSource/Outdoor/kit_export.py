
def build_everything():
    for o in list(bpy.data.objects):
        if o.type == 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)
    ensure_materials()
    MANIFEST.clear()
    items = []

    def add(obj, cat, zbias=0):
        register(obj, cat, zbias)
        items.append(obj)

    # ground
    add(ground_tile("Ground_A", 'A', 1), "ground")
    add(ground_tile("Ground_B", 'B', 2), "ground")
    add(ground_tile("Ground_C", 'C', 3), "ground")
    add(curb_front("Curb_Front", 4), "curb", -1)
    add(ground_back("Ground_Back_A", 62, 'A'), "groundback")
    add(ground_back("Ground_Back_B", 63, 'B'), "groundback")

    # cars (gameplay cover, fitted to footprint)
    add(sedan("Car_Sedan_Blue", P['car_blue'], 5), "car")
    add(sedan("Car_Sedan_Red", P['car_red'], 6), "car")
    add(sedan("Car_Sedan_White", P['car_white'], 16), "car")
    add(sedan("Car_Sedan_Grey", P['car_grey'], 17), "car")
    add(sedan("Car_Sedan_Burnt", P['car_dark'], 7, burnt=True), "car")
    add(van("Car_Van", P['car_white'], 8), "van")
    add(van("Car_Van_Yellow", P['car_yellow'], 18), "van")
    add(pickup("Car_Pickup", P['car_green'], 9), "pickup")
    add(pickup("Car_Pickup_Grey", P['car_grey'], 19), "pickup")

    # cover
    add(jersey("Barrier_Jersey", 10), "barrier")
    add(jersey("Barrier_Jersey_Broken", 11, broken=True), "barrier")
    add(sandbags("Sandbags", 12), "sandbags")
    add(dumpster("Dumpster", P['sign_green'], 13), "dumpster")
    add(dumpster("Dumpster_Blue", P['sign_blue'], 20), "dumpster")
    add(tire_pile("Tires", 14), "debris")
    add(cones("Cones", 15), "debris")

    # backdrop buildings
    add(building("Bld_Apartment_A", 'plaster_beige', 12, 6, 5, 4, 21,
                 missing=lambda f, i: f >= 3 and i >= 2, balconies=[(1, 1), (2, 1), (2, 3), (1, 3), (3, 0)], rubble=(1.0, 6.0, 2.2, 60)), "building", -1)
    add(building("Bld_Shop_Brick", 'brick', 10, 6, 3, 3, 22, shop=True, awning=True, shutters=True, balconies=[(1, 1)],
                 missing=lambda f, i: f == 2 and i == 2), "building", -1)
    add(building("Bld_Office_Tower", 'plaster_grey', 14, 7, 8, 6, 23, strip=True, fh=3.2, broken_roof=True,
                 missing=lambda f, i: f >= 6 and i >= 4 or (f == 7 and i == 3), rubble=(2.0, 7.0, 2.0, 50), roof_clutter=False), "building", -1)
    add(building("Bld_Collapsed_Brick", 'brick', 12, 6, 4, 4, 24, broken_roof=True,
                 missing=lambda f, i: (f >= 2 and i <= 1) or (f == 1 and i == 0) or (f == 3 and i == 2), rubble=(-6.0, 0.5, 3.0, 90)), "building", -1)
    add(building("Bld_Warehouse", 'concrete', 16, 9, 1, 4, 25, fh=7.5, sawtooth=True, shutters=True, shop=True), "building", -1)
    add(building("Bld_Burnt_Block", 'plaster_grey', 10, 6, 4, 3, 26, soot=True, missing=lambda f, i: f == 3 and i == 1, balconies=[(1, 0), (2, 2)]), "building", -1)
    add(building("Bld_Apartment_B", 'plaster_grey', 11, 6, 6, 4, 27, balconies=[(2, 0), (2, 2), (3, 1), (4, 3), (1, 2)],
                 missing=lambda f, i: f == 5 and i >= 2, curtain=0.5), "building", -1)
    add(building("Bld_Row_Brick", 'brick', 14, 6, 3, 5, 28, shop=True, awning=True, shutters=True,
                 missing=lambda f, i: f == 2 and i in (0, 1)), "building", -1)
    add(skyline("Sky_A", 31), "skyline", -1)
    add(skyline("Sky_B", 32, hmin=14, hmax=44), "skyline", -1)

    # street furniture / debris
    add(street_lamp("Prop_Lamp", 41), "prop")
    add(street_lamp("Prop_Lamp_Broken", 42, broken=True), "prop")
    add(traffic_light("Prop_TrafficLight", 43), "prop")
    add(road_sign("Prop_Sign_Stop", 44, 'stop'), "prop")
    add(road_sign("Prop_Sign_Warn", 45, 'warn'), "prop")
    add(road_sign("Prop_Sign_Direction", 64, 'dir'), "prop")
    add(power_pole("Prop_PowerPole", 46), "prop")
    add(dead_tree("Prop_Tree_Dead", 47), "prop")
    add(dead_tree("Prop_Tree_Dead_B", 48, 4.2), "prop")
    add(bench("Prop_Bench", 49), "prop")
    add(bus_shelter("Prop_BusShelter", 50), "prop")
    add(fence("Prop_Fence", 51), "prop")
    add(fence("Prop_Fence_Intact", 52, gap=False), "prop")
    add(hydrant("Prop_Hydrant", 53), "prop")
    add(trash_bags("Prop_TrashBags", 54), "debris")
    add(pallet_stack("Prop_Pallets", 55), "debris")
    add(rubble_heap("Prop_Rubble_A", 56), "debris")
    add(rubble_heap("Prop_Rubble_B", 57, w=2.6, d=2.0, h=0.9, count=22), "debris")
    add(rubble_heap("Prop_Rubble_Big", 58, w=5.0, d=3.4, h=1.8, count=60), "debris")
    add(slab_lean("Prop_SlabLean", 59), "debris")
    add(container("Prop_Container", P['car_blue'], 60), "prop")
    add(container("Prop_Container_Rust", P['rust'], 61), "prop")
    return items
