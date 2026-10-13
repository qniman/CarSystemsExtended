# The rest of the part models. Run: python bridge.py cse_models.py build_parts.py
# Optional: set ONLY = ["siren"] in a -c snippet before this file to build some.
#
# Built in part space with P()/S() so every model keeps the exact layout of its placeholder in
# PartCatalog.cs (origin, bolt holes, plug, LED, fuse slot): the mounts placed in game stay
# valid. Exported with center_on_bounds=False for the same reason.
import math

MM = 0.001
ROOT = r"C:\Users\User\source\CarSystemsExtended"
OUT = ROOT + r"\CarSystemsExtended\Assets\models\\"
PREV = ROOT + r"\tools\blender\previews\\"

scene = model_scene()

plastic = material("plastic_black", (0.03, 0.03, 0.033), roughness=0.55)
plastic_grey = material("plastic_grey", (0.09, 0.09, 0.1), roughness=0.6)
rubber = material("rubber", (0.025, 0.025, 0.025), roughness=0.95)
steel = material("steel", (0.55, 0.55, 0.57), roughness=0.35, metallic=1.0)
zinc = material("zinc", (0.62, 0.62, 0.58), roughness=0.45, metallic=1.0)
brass = material("brass", (0.75, 0.6, 0.3), roughness=0.35, metallic=1.0)
label = material("label_silver", (0.7, 0.7, 0.68), roughness=0.5)
label_red = material("label_red", (0.5, 0.05, 0.04), roughness=0.6)
ink = material("ink", (0.05, 0.05, 0.06), roughness=0.7)
lens = material("lens", (0.35, 0.04, 0.03), roughness=0.15)


def done(col, name, view=(35, 0, 30)):
    stats = export(col, OUT + name, center_on_bounds=False)
    preview(scene, col, PREV + name + ".png", view=view)
    return stats


def screw(col, name, x, y, z, axis='Y', r=1.6 * MM):
    """Pan-head screw, its head facing the part's +axis."""
    cylinder(col, name, r, 1.0 * MM, loc=P(x, y, z), axis=AX[axis], mat=zinc, segments=10)


# ---------------------------------------------------------------------------- alarm unit
def alarm_unit():
    col = fresh_collection(scene, "CSE alarm unit")
    # case 130 x 35 x 90, lid seam and ribbing
    box(col, "case", S(0.13, 0.035, 0.09), loc=P(0, 0, 0), mat=plastic, bevel_w=3 * MM, segs=2)
    box(col, "lid_seam", S(0.1315, 0.0015, 0.0915), loc=P(0, 0.006, 0), mat=plastic_grey, bevel_w=0.5 * MM, segs=1)
    for i in range(5):  # cooling ribs on top
        box(col, "rib_%d" % i, S(0.004, 0.0015, 0.05), loc=P(-0.05 + i * 0.008, 0.0182, 0.008), mat=plastic_grey,
            bevel_w=0.4 * MM, segs=1)
    # mounting ears with holes for the 7 mm bolts at x = +-0.085 (bolts go up from below)
    for side in (-1, 1):
        ear = box(col, "ear_%d" % side, S(0.035, 0.004, 0.03), loc=P(side * 0.08, -0.0155, 0), mat=zinc,
                  bevel_w=1.5 * MM, segs=2)
        hole = cylinder(col, "ear_hole_%d" % side, 2.6 * MM, 0.02, loc=P(side * 0.085, -0.0155, 0), axis=AX['Y'],
                        segments=12)
        cut(ear, hole)
    # label on top
    box(col, "label", S(0.06, 0.0004, 0.04), loc=P(0.025, 0.0177, -0.012), mat=label, bevel_w=0.1 * MM, segs=1)
    t = text(col, "label_brand", "VARTIJA 95", 6.5 * MM, loc=P(0.025, 0.0181, -0.004), mat=label_red, extrude=0.08 * MM)
    t.rotation_euler = (0, 0, math.pi)
    t = text(col, "label_specs", "12V  15A  MADE IN FINLAND", 2.4 * MM, loc=P(0.025, 0.0181, -0.02), mat=ink,
             extrude=0.05 * MM, bold=False)
    t.rotation_euler = (0, 0, math.pi)
    # harness connector at the back (plug), with pins
    box(col, "connector", S(0.04, 0.018, 0.012), loc=P(-0.04, 0, -0.05), mat=plastic_grey, bevel_w=1 * MM, segs=1)
    for i in range(6):
        cylinder(col, "pin_%d" % i, 0.6 * MM, 0.004, loc=P(-0.055 + i * 0.006, 0, -0.057), axis=AX['Z'],
                 mat=brass, segments=6)
    marker(col, "plug", P(-0.04, 0, -0.05))
    # fuse holder on top at the back right; the fuse plugs in at (0.045, 0.031, -0.03)
    holder = box(col, "fuse_holder", S(0.03, 0.008, 0.014), loc=P(0.045, 0.0215, -0.03), mat=plastic_grey,
                 bevel_w=1 * MM, segs=1)
    slot = box(col, "fuse_slot", S(0.021, 0.01, 0.007), loc=P(0.045, 0.026, -0.03))
    cut(holder, slot)
    marker(col, "fuse", P(0.045, 0.031, -0.03))
    # LED bezel on the front; the mod draws the glowing lens at the marker
    cylinder(col, "led_bezel", 4.5 * MM, 1.5 * MM, loc=P(0.03, 0.005, 0.0455), axis=AX['Z'], mat=zinc,
             segments=16, bevel_w=0.4 * MM, segs=1)
    marker(col, "led", P(0.03, 0.005, 0.047))
    # learn button and lid screws
    cylinder(col, "learn_button", 2.5 * MM, 1.2 * MM, loc=P(-0.03, 0.002, 0.0455), axis=AX['Z'], mat=rubber,
             segments=12)
    for sx in (-0.058, 0.058):
        for sz in (-0.038, 0.038):
            screw(col, "screw_%d_%d" % (sx * 1000, sz * 1000), sx, 0.0178, sz)
    return done(col, "alarm_unit", view=(40, 0, 210))


# ---------------------------------------------------------------------------- siren
def siren():
    col = fresh_collection(scene, "CSE siren")
    # driver housing at the back, horn flaring to the front, grille
    cylinder(col, "housing", 0.033, 0.03, loc=P(0, 0, -0.015), axis=AX['Z'], mat=plastic, segments=28,
             bevel_w=2.5 * MM, segs=2)
    cone(col, "horn", 0.03, 0.045, 0.03, loc=P(0, 0, 0.015), mat=plastic, segments=28, cap=False)
    torus(col, "rim", 0.045, 2 * MM, loc=P(0, 0, 0.03), axis=AX['Z'], mat=plastic, seg_major=28, seg_minor=6)
    cylinder(col, "grille", 0.039, 1.2 * MM, loc=P(0, 0, 0.026), axis=AX['Z'], mat=plastic_grey, segments=28)
    for i in range(-3, 4):  # grille bars
        box(col, "bar_%d" % (i + 3), S(0.074, 0.0025, 0.0015), loc=P(0, i * 0.009, 0.0275), mat=plastic)
    cylinder(col, "phase_plug", 0.012, 0.012, loc=P(0, 0, 0.022), axis=AX['Z'], mat=plastic, segments=16,
             bevel_w=3 * MM, segs=2)
    # L bracket: horizontal foot under, vertical leg up to the housing
    foot = box(col, "foot", S(0.03, 0.004, 0.06), loc=P(0, -0.05, -0.01), mat=zinc, bevel_w=1 * MM, segs=1)
    hole = cylinder(col, "foot_hole", 5.5 * MM, 0.02, loc=P(0, -0.05, -0.015), axis=AX['Y'], segments=12)
    cut(foot, hole)
    box(col, "leg", S(0.03, 0.05, 0.004), loc=P(0, -0.027, 0.018), mat=zinc, bevel_w=1 * MM, segs=1)
    cylinder(col, "pivot_bolt", 4 * MM, 0.04, loc=P(0, -0.006, 0.018), axis=AX['X'], mat=zinc, segments=10)
    # terminals at the back
    box(col, "terminal_block", S(0.02, 0.012, 0.008), loc=P(0, 0.005, -0.033), mat=plastic_grey, bevel_w=1 * MM, segs=1)
    for sx in (-0.005, 0.005):
        box(col, "spade_%d" % (sx * 1000), S(0.0035, 0.007, 0.006), loc=P(sx, 0.005, -0.039), mat=brass)
    marker(col, "plug", P(0, 0, -0.035))
    t = text(col, "brand", "VARTIJA", 7 * MM, loc=P(0, 0.034, -0.015), mat=label_red, extrude=0.1 * MM)
    t.rotation_euler = (0, 0, math.pi)
    return done(col, "siren", view=(25, 0, 30))


# ---------------------------------------------------------------------------- dash LED
def status_led():
    col = fresh_collection(scene, "CSE status led")
    cylinder(col, "base", 9 * MM, 1.5 * MM, loc=P(0, -0.0025, 0), axis=AX['Y'], mat=plastic, segments=20,
             bevel_w=0.6 * MM, segs=2)
    cylinder(col, "body", 6 * MM, 0.01, loc=P(0, 0.003, 0), axis=AX['Y'], mat=plastic, segments=20,
             bevel_w=0.8 * MM, segs=1)
    torus(col, "bezel", 5.2 * MM, 1 * MM, loc=P(0, 0.008, 0), axis=AX['Y'], mat=zinc, seg_major=20, seg_minor=6)
    marker(col, "led", P(0, 0.009, 0))
    cylinder(col, "lead", 1.2 * MM, 0.008, loc=P(0, -0.004, -0.008), axis=AX['Z'], mat=rubber, segments=8)
    marker(col, "plug", P(0, -0.004, -0.012))
    return done(col, "status_led", view=(45, 0, 30))


# ---------------------------------------------------------------------------- choke servo
def choke_servo():
    col = fresh_collection(scene, "CSE choke servo")
    # motor + gearbox housing, two-tone
    box(col, "gearbox", S(0.06, 0.025, 0.05), loc=P(0, 0.0075, 0), mat=plastic_grey, bevel_w=2.5 * MM, segs=2)
    box(col, "bottom", S(0.058, 0.016, 0.048), loc=P(0, -0.012, 0), mat=plastic, bevel_w=2 * MM, segs=2)
    cylinder(col, "motor", 0.012, 0.03, loc=P(-0.012, 0.012, -0.026), axis=AX['Z'], mat=steel, segments=16)
    # mounting flange at the front with the 8 mm bolt hole at (0, -0.0225, 0.035)
    flange = box(col, "flange", S(0.03, 0.003, 0.025), loc=P(0, -0.0215, 0.03), mat=zinc, bevel_w=1 * MM, segs=1)
    hole = cylinder(col, "flange_hole", 3 * MM, 0.02, loc=P(0, -0.0215, 0.035), axis=AX['Y'], segments=12)
    cut(flange, hole)
    # output lever on the side and the choke cable
    cylinder(col, "shaft", 3 * MM, 0.008, loc=P(0.033, 0.01, 0), axis=AX['X'], mat=zinc, segments=10)
    box(col, "lever", S(0.004, 0.035, 0.008), loc=P(0.037, 0.022, 0), mat=zinc, bevel_w=1 * MM, segs=1)
    cylinder(col, "cable_clamp", 3 * MM, 0.008, loc=P(0.045, 0.025, 0), axis=AX['X'], mat=zinc, segments=10)
    cylinder(col, "cable", 1.6 * MM, 0.05, loc=P(0.072, 0.025, 0), axis=AX['X'], mat=rubber, segments=8)
    # connector on the other side
    box(col, "connector", S(0.006, 0.012, 0.014), loc=P(-0.033, 0, 0), mat=plastic_grey, bevel_w=0.8 * MM, segs=1)
    marker(col, "plug", P(-0.033, 0, 0))
    t = text(col, "brand", "VARTIJA", 5 * MM, loc=P(0, 0.0202, -0.005), mat=label_red, extrude=0.08 * MM)
    t.rotation_euler = (0, 0, math.pi)
    return done(col, "choke_servo", view=(35, 0, 220))


# ---------------------------------------------------------------------------- lock actuator
def lock_actuator():
    col = fresh_collection(scene, "CSE lock actuator")
    cylinder(col, "body", 0.015, 0.07, loc=P(0, 0, -0.002), axis=AX['Z'], mat=plastic, segments=20,
             bevel_w=2 * MM, segs=2)
    for z in (-0.025, -0.012, 0.0, 0.012):
        torus(col, "rib_%d" % int(z * 1000), 0.0152, 0.8 * MM, loc=P(0, 0, z), axis=AX['Z'], mat=plastic_grey,
              seg_major=20, seg_minor=4)
    cylinder(col, "end_cap", 0.012, 0.008, loc=P(0, 0, 0.034), axis=AX['Z'], mat=plastic_grey, segments=20,
             bevel_w=1.5 * MM, segs=1)
    # rod going up through a rubber boot
    boot = cone(col, "boot", 6 * MM, 4 * MM, 0.012, loc=P(0, 0.016, 0.03), mat=rubber, segments=12)
    boot.rotation_euler = (math.radians(-90), 0, 0)  # boot axis up
    cylinder(col, "rod", 2.5 * MM, 0.04, loc=P(0, 0.035, 0.03), axis=AX['Y'], mat=zinc, segments=10)
    # flange on the cabin side (+X) with the 6 mm bolt hole at (0.019, 0, 0)
    flange = box(col, "flange", S(0.004, 0.03, 0.05), loc=P(0.017, 0, 0), mat=zinc, bevel_w=1.2 * MM, segs=1)
    hole = cylinder(col, "flange_hole", 2.4 * MM, 0.02, loc=P(0.017, 0, 0), axis=AX['X'], segments=12)
    cut(flange, hole)
    box(col, "connector", S(0.012, 0.01, 0.008), loc=P(0, 0, -0.042), mat=plastic_grey, bevel_w=0.8 * MM, segs=1)
    marker(col, "plug", P(0, 0, -0.045))
    return done(col, "lock_actuator", view=(30, 0, 60))


# ---------------------------------------------------------------------------- fuse
def fuse():
    col = fresh_collection(scene, "CSE fuse")
    blue = material("fuse_body", (0.08, 0.22, 0.75), roughness=0.25)
    box(col, "body", S(0.019, 0.012, 0.005), loc=P(0, 0, 0), mat=blue, bevel_w=0.8 * MM, segs=2)
    box(col, "grip", S(0.016, 0.003, 0.0055), loc=P(0, 0.0045, 0), mat=blue, bevel_w=0.5 * MM, segs=1)
    for sx in (-0.005, 0.005):
        box(col, "blade_%d" % (sx * 1000), S(0.003, 0.008, 0.0008), loc=P(sx, -0.0095, 0), mat=zinc)
    t = text(col, "rating", "15", 3.5 * MM, loc=P(0, 0.0047, 0.0029), mat=label, extrude=0.05 * MM)
    t.rotation_euler = (math.radians(90), 0, 0)
    return done(col, "fuse", view=(20, 0, 20))


# ---------------------------------------------------------------------------- A23 battery
def a23_battery():
    col = fresh_collection(scene, "CSE a23")
    blue = material("battery_label", (0.06, 0.18, 0.55), roughness=0.4)
    cylinder(col, "label", 5 * MM, 0.022, loc=P(0, 0, 0), axis=AX['Y'], mat=blue, segments=16)
    cylinder(col, "cap_minus", 4.9 * MM, 1.2 * MM, loc=P(0, -0.0115, 0), axis=AX['Y'], mat=zinc, segments=16,
             bevel_w=0.4 * MM, segs=1)
    cylinder(col, "cap_plus", 4.9 * MM, 1.2 * MM, loc=P(0, 0.0115, 0), axis=AX['Y'], mat=zinc, segments=16,
             bevel_w=0.4 * MM, segs=1)
    cylinder(col, "nub", 2 * MM, 1 * MM, loc=P(0, 0.0128, 0), axis=AX['Y'], mat=zinc, segments=12)
    t = text(col, "type", "A23 12V", 2.6 * MM, loc=P(0, 0, 0.0052), mat=label, extrude=0.04 * MM, bold=False)
    t.rotation_euler = (math.radians(90), 0, math.radians(90))
    return done(col, "a23_battery", view=(15, 0, 20))


# ---------------------------------------------------------------------------- parcel
def parcel():
    col = fresh_collection(scene, "CSE parcel")
    card = material("cardboard", (0.36, 0.22, 0.1), roughness=0.9)
    tape = material("tape", (0.62, 0.5, 0.32), roughness=0.4)
    paper = material("paper", (0.86, 0.84, 0.76), roughness=0.85)
    box(col, "box", S(0.4, 0.22, 0.3), loc=P(0, 0, 0), mat=card, bevel_w=2 * MM, segs=1)
    box(col, "flap_seam", S(0.4005, 0.0008, 0.002), loc=P(0, 0.1101, 0), mat=ink)
    box(col, "tape_top", S(0.06, 0.001, 0.302), loc=P(0, 0.1103, 0), mat=tape)
    box(col, "tape_front", S(0.06, 0.06, 0.001), loc=P(0, 0.08, 0.1503), mat=tape)
    box(col, "tape_back", S(0.06, 0.06, 0.001), loc=P(0, 0.08, -0.1503), mat=tape)
    # address label and the cash-on-delivery sticker
    box(col, "label", S(0.12, 0.0008, 0.08), loc=P(-0.11, 0.1104, 0.06), mat=paper)
    t = text(col, "label_from", "AUTOTARVIKE POSTIMYYNTI OY", 5 * MM, loc=P(-0.11, 0.1108, 0.085), mat=ink,
             extrude=0.1 * MM, bold=False)
    t = text(col, "label_town", "LOIVOS", 9 * MM, loc=P(-0.11, 0.1108, 0.06), mat=ink, extrude=0.1 * MM)
    box(col, "cod", S(0.09, 0.0008, 0.05), loc=P(0.12, 0.1104, -0.08), mat=label_red)
    t = text(col, "cod_text", "POSTIENNAKKO", 9 * MM, loc=P(0.12, 0.1108, -0.08), mat=paper, extrude=0.1 * MM)
    t = text(col, "fragile", "AUTOTARVIKE", 0.03, loc=P(0, 0, 0.1505), mat=label_red, extrude=0.2 * MM)
    t.rotation_euler = (math.radians(90), 0, 0)
    return done(col, "parcel", view=(35, 0, 25))


# ---------------------------------------------------------------------------- envelope
def envelope():
    col = fresh_collection(scene, "CSE envelope")
    paper = material("paper", (0.86, 0.84, 0.76), roughness=0.85)
    box(col, "sheet", S(0.22, 0.004, 0.11), loc=P(0, 0, 0), mat=paper, bevel_w=0.4 * MM, segs=1)
    # flap outline on the back, stamp and address on the front (top)
    for sx, rot in ((-0.055, 26), (0.055, -26)):
        b = box(col, "flap_%d" % int(sx * 1000), S(0.125, 0.0006, 0.0012), loc=P(sx, -0.0022, 0.02), mat=plastic_grey)
        b.rotation_euler = (0, 0, math.radians(rot))
    box(col, "stamp", S(0.025, 0.0006, 0.03), loc=P(-0.085, 0.0022, 0.03), mat=label_red)
    t = text(col, "stamp_text", "SUOMI", 4 * MM, loc=P(-0.085, 0.0026, 0.025), mat=paper, extrude=0.05 * MM)
    for i, line in enumerate(("AUTOTARVIKE POSTIMYYNTI OY", "TILAUKSET", "LOIVOS")):
        t = text(col, "addr_%d" % i, line, 5 * MM, loc=P(0.02, 0.0026, 0.005 - i * 0.012), mat=ink, extrude=0.05 * MM,
                 bold=(i == 0))
        return done(col, "envelope", view=(55, 0, 0))


BUILDERS = {
    "alarm_unit": alarm_unit, "siren": siren, "status_led": status_led, "choke_servo": choke_servo,
    "lock_actuator": lock_actuator, "fuse": fuse, "a23_battery": a23_battery, "parcel": parcel,
    "envelope": envelope,
}
try:
    names = ONLY  # noqa: F821 - optional, set by a snippet sent before this file
except NameError:
    names = list(BUILDERS)
result = {n: BUILDERS[n]() for n in names}
