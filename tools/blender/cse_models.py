"""
Helpers for building Car Systems Extended part models in Blender (run through bridge.py, or
`blender --background --python`). Pure data API (no bpy.ops) so it works from the add-on's
timer context too.

Conventions (see MODELS.md):
  * metres; the part's top is Blender +Z, its front is Blender -Y, origin = centre of bounds
  * marker = name -> point, exported as `#marker <name> x y z`
  * export writes OBJ already in the mod's part space (Unity, left-handed):
        part.x = -blender.x, part.y = blender.z, part.z = -blender.y
    (a reflection between the two handednesses, so triangle winding is reversed), plus an MTL.
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

# ---------------------------------------------------------------------------- scene

def model_scene(name="CSE Models"):
    """A dedicated scene so the user's own scene is left alone; made current in the window."""
    scene = bpy.data.scenes.get(name) or bpy.data.scenes.new(name)
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.length_unit = 'MILLIMETERS'
    for w in bpy.context.window_manager.windows:
        w.scene = scene
    return scene


def fresh_collection(scene, name):
    """Empty collection `name` linked to the scene (old contents removed)."""
    col = bpy.data.collections.get(name)
    if col is not None:
        for ob in list(col.objects):
            data = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if data is not None and getattr(data, "users", 1) == 0:
                if isinstance(data, bpy.types.Mesh):
                    bpy.data.meshes.remove(data)
                elif isinstance(data, bpy.types.Curve):
                    bpy.data.curves.remove(data)
    else:
        col = bpy.data.collections.new(name)
    if col.name not in scene.collection.children:
        scene.collection.children.link(col)
    return col


# ---------------------------------------------------------------------------- materials

def material(name, rgb, roughness=0.6, metallic=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)
    m.metallic = metallic
    m.roughness = roughness
    if hasattr(m, "use_nodes"):
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF") if m.node_tree else None
        if bsdf is not None:
            bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
            bsdf.inputs["Roughness"].default_value = roughness
            bsdf.inputs["Metallic"].default_value = metallic
            if "Alpha" in bsdf.inputs:
                bsdf.inputs["Alpha"].default_value = alpha
    return m


# ---------------------------------------------------------------------------- geometry

def _object(col, name, mesh, mat=None, loc=(0, 0, 0)):
    ob = bpy.data.objects.new(name, mesh)
    ob.location = loc
    if mat is not None:
        ob.data.materials.append(mat)
    col.objects.link(ob)
    return ob


def bevel(ob, width, segments=3, limit_angle=math.radians(40)):
    mod = ob.modifiers.new("bevel", 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    mod.angle_limit = limit_angle
    mod.harden_normals = False
    return mod


def smooth(ob, angle=math.radians(35)):
    """Smooth shading with sharp edges kept (auto-smooth by angle)."""
    me = ob.data
    if hasattr(me, "set_sharp_from_angle"):
        me.set_sharp_from_angle(angle=angle)  # Blender 4.1+: shades smooth, keeps hard edges
    else:
        for p in me.polygons:
            p.use_smooth = True
    return ob


def box(col, name, size, loc=(0, 0, 0), mat=None, bevel_w=0.0, segs=3):
    """Axis-aligned box `size` = (x, y, z) centred on `loc`."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = _object(col, name, me, mat, loc)
    if bevel_w > 0:
        bevel(ob, bevel_w, segs)
    return smooth(ob)


def cylinder(col, name, radius, depth, loc=(0, 0, 0), axis='Z', mat=None, segments=24, bevel_w=0.0, segs=2):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                          radius1=radius, radius2=radius, depth=depth)
    if axis == 'X':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'))
    elif axis == 'Y':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = _object(col, name, me, mat, loc)
    if bevel_w > 0:
        bevel(ob, bevel_w, segs)
    return smooth(ob)


def dome(col, name, radius, height, loc=(0, 0, 0), mat=None, segments=16, rings=5):
    """Spherical cap sitting on z=0 (a LED lens, a button top)."""
    bm = bmesh.new()
    verts = []
    for r in range(rings + 1):
        t = r / rings
        z = height * math.sin(t * math.pi / 2)
        rad = radius * math.cos(t * math.pi / 2)
        ring = []
        for s in range(segments if r < rings else 1):
            a = 2 * math.pi * s / segments
            ring.append(bm.verts.new((rad * math.cos(a), rad * math.sin(a), z)))
        verts.append(ring)
    for r in range(rings - 1):
        a, b = verts[r], verts[r + 1]
        for s in range(segments):
            bm.faces.new((a[s], a[(s + 1) % segments], b[(s + 1) % segments], b[s]))
    top = verts[rings][0]
    last = verts[rings - 1]
    for s in range(segments):
        bm.faces.new((last[s], last[(s + 1) % segments], top))
    bm.faces.new(list(reversed(verts[0])))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return smooth(_object(col, name, me, mat, loc), math.radians(80))


def torus(col, name, major, minor, loc=(0, 0, 0), mat=None, seg_major=32, seg_minor=8,
          sweep=2 * math.pi, start=0.0, axis='Z', caps=False):
    """Torus (or an arc of one: `sweep` < 2 pi) around `axis`, for rings and padlock shackles."""
    bm = bmesh.new()
    closed = sweep >= 2 * math.pi - 1e-6
    n = seg_major if closed else seg_major + 1
    rings = []
    for i in range(n):
        a = start + sweep * i / seg_major
        c, s = math.cos(a), math.sin(a)
        ring = []
        for j in range(seg_minor):
            b = 2 * math.pi * j / seg_minor
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * c, r * s, minor * math.sin(b))))
        rings.append(ring)
    for i in range(n if closed else n - 1):
        a, b = rings[i], rings[(i + 1) % n]
        for j in range(seg_minor):
            bm.faces.new((a[j], b[j], b[(j + 1) % seg_minor], a[(j + 1) % seg_minor]))
    if caps and not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    if axis == 'X':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'))
    elif axis == 'Y':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return smooth(_object(col, name, me, mat, loc), math.radians(80))


def cut(target, cutter):
    """Boolean difference (applied at export through the evaluated mesh); hides the cutter."""
    mod = target.modifiers.new("cut_" + cutter.name, 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.object = cutter
    # stays evaluated (a viewport-disabled cutter would be dropped from the depsgraph)
    cutter.hide_render = True
    cutter.display_type = 'WIRE'
    cutter["cse_cutter"] = True
    # the bevel must run after the cut to round its edges
    for m in list(target.modifiers):
        if m.type == 'BEVEL':
            target.modifiers.move(target.modifiers.find(m.name), len(target.modifiers) - 1)
    return mod


def text(col, name, body, size, loc=(0, 0, 0), mat=None, extrude=0.0002, rot=(0, 0, 0), bold=True):
    """Flat lettering lying on the XY plane, centred on `loc`."""
    cu = bpy.data.curves.new(name, 'FONT')
    cu.body = body
    cu.size = size
    cu.extrude = extrude
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.resolution_u = 1
    if bold:
        cu.offset = size * 0.012
    ob = bpy.data.objects.new(name, cu)
    ob.location = loc
    ob.rotation_euler = rot
    if mat is not None:
        cu.materials.append(mat)
    col.objects.link(ob)
    return ob


def marker(col, name, loc):
    """A tiny cube whose centre is a named point (bolt, plug, led, ring...)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=0.002)
    me = bpy.data.meshes.new("marker_" + name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.location = loc
    ob["cse_marker"] = True
    ob.display_type = 'WIRE'
    ob.hide_render = True
    col.objects.link(ob)
    return ob


# ---------------------------------------------------------------------------- export

def _to_part(v):
    # Blender is right-handed, Unity left-handed. With the part facing -Y in Blender, the holder's
    # right is Blender -X; in the mod it is +X. So x flips too: a reflection in numbers, the same
    # physical object - which is why the winding is reversed below.
    return (-v.x, v.z, -v.y)


def export(col, path_noext, center_on_bounds=True):
    """
    Writes <path>.obj (+ .mtl) of every visible mesh/curve object in `col`, in part space, and
    the markers as comments. Returns stats (triangles, size, markers).
    """
    deps = bpy.context.evaluated_depsgraph_get()
    geo = [o for o in col.objects if o.type in ('MESH', 'CURVE', 'FONT')
           and not o.get("cse_marker") and not o.get("cse_cutter")]
    markers = [o for o in col.objects if o.get("cse_marker")]

    # gather triangles
    tris = []   # (material name, [(pos, normal) x3])
    mats = {}
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in geo:
        ev = ob.evaluated_get(deps)
        me = ev.to_mesh()
        me.calc_loop_triangles()
        mw = ob.matrix_world
        nw = mw.to_3x3().inverted().transposed()
        cn = me.corner_normals
        for t in me.loop_triangles:
            mat = me.materials[t.material_index] if len(me.materials) > t.material_index else None
            mname = mat.name if mat else "default"
            if mat is not None:
                mats[mname] = mat
            corners = []
            for k in range(3):
                li = t.loops[k]
                p = mw @ me.vertices[me.loops[li].vertex_index].co
                n = (nw @ cn[li].vector).normalized()
                corners.append((p, n))
                lo = Vector(map(min, lo, p))
                hi = Vector(map(max, hi, p))
            tris.append((mname, corners))
        ev.to_mesh_clear()

    center = (lo + hi) / 2 if center_on_bounds else Vector()

    os.makedirs(os.path.dirname(path_noext), exist_ok=True)
    name = os.path.basename(path_noext)
    with open(path_noext + ".mtl", "w", encoding="utf-8") as f:
        for mname, m in mats.items():
            c = m.diffuse_color
            f.write("newmtl {}\nKd {:.4f} {:.4f} {:.4f}\nd {:.3f}\nNs {:.1f}\nPm {:.3f}\n\n".format(
                mname, c[0], c[1], c[2], c[3], (1 - m.roughness) * 100, m.metallic))

    with open(path_noext + ".obj", "w", encoding="utf-8") as f:
        f.write("# Car Systems Extended part model '{}', part space (Unity, left-handed)\n".format(name))
        f.write("mtllib {}.mtl\n".format(name))
        for mk in markers:
            p = _to_part(mk.matrix_world.translation - center)
            # Blender names are global: a second "ring" becomes "ring.001"
            f.write("#marker {} {:.5f} {:.5f} {:.5f}\n".format(mk.name.split(".")[0], *p))
        # shared positions / normals, written once
        pos_index, nrm_index = {}, {}
        pos_lines, nrm_lines, faces = [], [], []
        by_mat = {}
        for mname, corners in tris:
            by_mat.setdefault(mname, []).append(corners)

        def idx(table, lines, fmt, vec, digits):
            key = tuple(round(c, digits) for c in vec)
            i = table.get(key)
            if i is None:
                lines.append(fmt.format(*key))
                i = table[key] = len(lines)
            return i

        for mname, group in by_mat.items():
            faces.append("usemtl {}\n".format(mname))
            for corners in group:
                ids = [(idx(pos_index, pos_lines, "v {:.5f} {:.5f} {:.5f}\n", _to_part(p - center), 5),
                        idx(nrm_index, nrm_lines, "vn {:.3f} {:.3f} {:.3f}\n", _to_part(n), 3)) for p, n in corners]
                # reversed winding: the axis change is a reflection (Unity front normal = cross(b-a, c-a))
                faces.append("f {}//{} {}//{} {}//{}\n".format(*ids[2], *ids[1], *ids[0]))
        f.writelines(pos_lines)
        f.writelines(nrm_lines)
        f.writelines(faces)

    size = hi - lo
    return {
        "triangles": len(tris),
        "size_mm": [round(size.x * 1000, 1), round(size.y * 1000, 1), round(size.z * 1000, 1)],
        "markers": [m.name for m in markers],
        "materials": list(mats.keys()),
        "file": path_noext + ".obj",
    }


# ---------------------------------------------------------------------------- preview

def preview(scene, col, path_png, view=(35, 0, 30), distance=None, res=(900, 700)):
    """Renders `col` alone with a three-point light to a PNG for review."""
    for c in scene.collection.children:
        c.hide_render = c != col

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in col.objects:
        if o.get("cse_marker") or o.get("cse_cutter"):
            continue
        for corner in o.bound_box:
            w = o.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    center = (lo + hi) / 2
    radius = max((hi - lo).length / 2, 0.01)
    dist = distance or radius * 3.2

    rig = fresh_collection(scene, "CSE Preview Rig")
    rig.hide_render = False
    cam_data = bpy.data.cameras.new("cse_cam")
    cam_data.lens = 70
    cam_data.clip_start = 0.001
    cam = bpy.data.objects.new("cse_cam", cam_data)
    rig.objects.link(cam)
    pitch, _, yaw = [math.radians(a) for a in view]
    direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = center + direction * dist
    cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    for nm, energy, off in (("key", 6.0, (1, -1, 1.5)), ("fill", 2.0, (-1.5, -0.5, 0.5)), ("rim", 4.0, (0, 1.5, 1))):
        ld = bpy.data.lights.new("cse_" + nm, 'AREA')
        ld.energy = energy * 0.35 * (radius * 10) ** 2
        ld.size = radius * 2
        lo_ = bpy.data.objects.new("cse_" + nm, ld)
        lo_.location = center + Vector(off).normalized() * dist
        lo_.rotation_euler = (center - lo_.location).to_track_quat('-Z', 'Y').to_euler()
        rig.objects.link(lo_)

    world = scene.world or bpy.data.worlds.new("cse_world")
    scene.world = world
    world.color = (0.18, 0.19, 0.2)
    if world.use_nodes and world.node_tree:
        bg = world.node_tree.nodes.get("Background")
        if bg:
            bg.inputs[0].default_value = (0.18, 0.19, 0.2, 1)
            bg.inputs[1].default_value = 0.6

    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.view_settings.exposure = 0.0
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.filepath = path_png
    scene.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True, scene=scene.name)
    for c in scene.collection.children:
        c.hide_render = False
    return path_png
