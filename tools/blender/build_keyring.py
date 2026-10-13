# Split keyring, hanging from the key's bow. Run: python bridge.py cse_models.py build_keyring.py
# Origin = the point where it passes through the key's hole. It hangs along part +Z
# (Blender -Y), like the remote: the mod swings it as a pendulum from the origin.
import math

MM = 0.001
ROOT = r"C:\Users\User\source\CarSystemsExtended"

scene = model_scene()
col = fresh_collection(scene, "CSE keyring")

ring_m = material("steel", (0.55, 0.55, 0.57), roughness=0.35, metallic=1.0)

R = 12 * MM          # ring radius
WIRE = 0.7 * MM

# Two turns of a split ring in the YZ plane, centred R below the hanging point (Blender -Y).
torus(col, "turn_1", R, WIRE, loc=(0, -R, 0), mat=ring_m, seg_major=32, seg_minor=6, axis='X')
torus(col, "turn_2", R, WIRE, loc=(0.85 * MM, -R, 0), mat=ring_m, seg_major=32, seg_minor=6, axis='X',
      sweep=math.radians(320), start=math.radians(110), caps=True)

marker(col, "fob", (0, -2 * R, 0))     # lowest point: the remote hangs here
marker(col, "center", (0, -R, 0))      # centre of mass, for the pendulum length

stats = export(col, ROOT + r"\CarSystemsExtended\Assets\models\keyring", center_on_bounds=False)
preview(scene, col, ROOT + r"\tools\blender\previews\keyring.png", view=(20, 0, 60))
result = stats
