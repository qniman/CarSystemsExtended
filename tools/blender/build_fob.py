# VARTIJA 95 alarm remote. Run: python bridge.py cse_models.py build_fob.py
# (cse_models.py is concatenated in front, so its helpers are in scope.)
import math

MM = 0.001
ROOT = r"C:\Users\User\source\CarSystemsExtended"

scene = model_scene()
col = fresh_collection(scene, "CSE fob")

body_m = material("fob_body", (0.035, 0.035, 0.04), roughness=0.45)
rubber_m = material("fob_rubber", (0.09, 0.09, 0.1), roughness=0.9)
red_m = material("fob_rubber_red", (0.42, 0.04, 0.03), roughness=0.85)
icon_m = material("fob_icon", (0.8, 0.8, 0.78), roughness=0.6)
led_m = material("fob_led", (0.9, 0.05, 0.03), roughness=0.2)
print_m = material("fob_print", (0.78, 0.76, 0.7), roughness=0.7)
steel_m = material("steel", (0.55, 0.55, 0.57), roughness=0.35, metallic=1.0)

T = 6 * MM  # half thickness: top surface at z = +T

# --- shell: a pebble-like rounded box, front at -Y
box(col, "body", (32 * MM, 56 * MM, 12 * MM), mat=body_m, bevel_w=4.5 * MM, segs=3)

# --- keyring lug at the back, with its hole
lug = box(col, "lug", (14 * MM, 11 * MM, 5 * MM), loc=(0, 31 * MM, -1.5 * MM), mat=body_m, bevel_w=2.2 * MM, segs=2)
hole = cylinder(col, "lug_hole", 2.3 * MM, 20 * MM, loc=(0, 32.5 * MM, -1.5 * MM), segments=12)
cut(lug, hole)
marker(col, "ring", (0, 32.5 * MM, -1.5 * MM))

# --- rubber keypad, slightly proud of the top
box(col, "keypad", (24 * MM, 34 * MM, 1.2 * MM), loc=(0, -6 * MM, T - 0.2 * MM), mat=rubber_m, bevel_w=0.5 * MM, segs=2)

def button(name, x, y, mat, r=4.2 * MM):
    cylinder(col, name, r, 1.6 * MM, loc=(x, y, T + 1.0 * MM), mat=mat, segments=16, bevel_w=0.6 * MM, segs=1)
    return T + 1.8 * MM  # top of the button

top_l = button("btn_lock", 6 * MM, -14 * MM, rubber_m)
top_u = button("btn_unlock", -6 * MM, -14 * MM, rubber_m)
top_s = button("btn_start", 0, 1 * MM, red_m, r=4.8 * MM)

def padlock(prefix, x, y, z, open_shackle):
    # body of the padlock, then the shackle arc towards the front (-Y)
    box(col, prefix + "_body", (3.4 * MM, 2.6 * MM, 0.35 * MM), loc=(x, y + 0.6 * MM, z), mat=icon_m)
    sx = 0.9 * MM if open_shackle else 0.0
    sy = -0.9 * MM if open_shackle else 0.0
    torus(col, prefix + "_shackle", 1.05 * MM, 0.28 * MM, loc=(x + sx, y - 0.7 * MM + sy, z),
          mat=icon_m, seg_major=10, seg_minor=6, sweep=math.pi, start=math.pi, caps=True)

padlock("icon_lock", 6 * MM, -14 * MM, top_l + 0.1 * MM, False)
padlock("icon_unlock", -6 * MM, -14 * MM, top_u + 0.1 * MM, True)

# remote start: a circular arrow
torus(col, "icon_start_arc", 1.9 * MM, 0.3 * MM, loc=(0, 1 * MM, top_s + 0.1 * MM), mat=icon_m,
      seg_major=14, seg_minor=6, sweep=math.radians(290), start=math.radians(115), caps=True)
tip = cylinder(col, "icon_start_tip", 0.9 * MM, 0.35 * MM, loc=(-0.95 * MM, 2.85 * MM, top_s + 0.1 * MM),
               mat=icon_m, segments=3)
tip.rotation_euler = (0, 0, math.radians(200))

# --- LED lens at the front
dome(col, "led_lens", 1.7 * MM, 1.0 * MM, loc=(0, -24.5 * MM, T - 0.3 * MM), mat=led_m, segments=10, rings=3)
marker(col, "led", (0, -24.5 * MM, T + 0.3 * MM))

# --- brand print between the start button and the lug
text(col, "brand", "VARTIJA 95", 3.0 * MM, loc=(0, 16 * MM, T + 0.02 * MM), mat=print_m, extrude=0.06 * MM, rot=(0, 0, math.pi))
text(col, "model", "remote", 1.6 * MM, loc=(0, 20.5 * MM, T + 0.02 * MM), mat=print_m, extrude=0.05 * MM, bold=False, rot=(0, 0, math.pi))

# --- battery cover screw underneath
cylinder(col, "screw", 1.1 * MM, 0.6 * MM, loc=(0, 8 * MM, -T + 0.1 * MM), mat=steel_m, segments=12)
box(col, "cover_seam", (20 * MM, 24 * MM, 0.15 * MM), loc=(0, 6 * MM, -T + 0.05 * MM), mat=rubber_m, bevel_w=0.05 * MM, segs=1)

stats = export(col, ROOT + r"\CarSystemsExtended\Assets\models\fob")
preview(scene, col, ROOT + r"\tools\blender\previews\fob_top.png", view=(50, 0, 200))
preview(scene, col, ROOT + r"\tools\blender\previews\fob_back.png", view=(15, 0, 330))
result = stats
