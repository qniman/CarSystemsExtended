# Satsuma key (no ring: the keyring is its own swinging model, build_keyring.py).
# Run: python bridge.py cse_models.py build_key.py
# Origin = centre of the hole in the bow, where the ring hangs; the blade runs along Blender +Y
# (part -Z).
import math

MM = 0.001
ROOT = r"C:\Users\User\source\CarSystemsExtended"

scene = model_scene()
col = fresh_collection(scene, "CSE key")

steel_m = material("key_steel", (0.62, 0.6, 0.55), roughness=0.3, metallic=1.0)
bow_m = material("key_bow", (0.03, 0.03, 0.032), roughness=0.55)

marker(col, "ring", (0, 0, 0))  # the keyring hangs here

# --- plastic bow, flat in XY, with the ring hole at the origin
BOW_Y = 9.5 * MM
bow = box(col, "bow", (21 * MM, 19 * MM, 4 * MM), loc=(0, BOW_Y, 0), mat=bow_m, bevel_w=3.5 * MM, segs=3)
bow_hole = cylinder(col, "bow_hole", 2.4 * MM, 10 * MM, loc=(0, 0, 0), segments=12)
cut(bow, bow_hole)
for i in range(4):  # grip ridges across the bow
    box(col, "ridge_%d" % i, (14 * MM, 0.8 * MM, 0.5 * MM), loc=(0, BOW_Y + (i - 1.5) * 2.4 * MM + 1 * MM, 2.05 * MM),
        mat=bow_m, bevel_w=0.2 * MM, segs=1)
box(col, "collar", (11 * MM, 2.5 * MM, 3.2 * MM), loc=(0, BOW_Y + 10.5 * MM, 0), mat=steel_m, bevel_w=0.6 * MM, segs=1)

# --- blade with a groove and cuts on one edge
BLADE_Y0 = BOW_Y + 11.5 * MM
BLADE_L = 38 * MM
blade = box(col, "blade", (8 * MM, BLADE_L, 2 * MM), loc=(0, BLADE_Y0 + BLADE_L / 2, 0), mat=steel_m,
            bevel_w=0.35 * MM, segs=1)
groove = box(col, "groove", (1.6 * MM, BLADE_L - 6 * MM, 1.2 * MM), loc=(-1 * MM, BLADE_Y0 + BLADE_L / 2 - 1 * MM, 1.1 * MM))
cut(blade, groove)
for i, depth in enumerate((2.2, 1.2, 2.6, 1.6, 2.0, 1.0)):
    tooth = box(col, "cut_%d" % i, (depth * MM * 2, 3.2 * MM, 4 * MM),
                loc=(4 * MM, BLADE_Y0 + 6 * MM + i * 5.2 * MM, 0))
    tooth.rotation_euler = (0, 0, math.radians(45))
    cut(blade, tooth)
tip = box(col, "tip_cut", (6 * MM, 6 * MM, 4 * MM), loc=(-4.2 * MM, BLADE_Y0 + BLADE_L + 0.5 * MM, 0))
tip.rotation_euler = (0, 0, math.radians(45))
cut(blade, tip)

stats = export(col, ROOT + r"\CarSystemsExtended\Assets\models\key", center_on_bounds=False)
preview(scene, col, ROOT + r"\tools\blender\previews\key.png", view=(55, 0, 210))
result = stats
