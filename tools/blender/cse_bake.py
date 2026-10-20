# Baked texture for a built part: run after cse_models.py and a build script, e.g.
#   python bridge.py cse_models.py <only.py> build_parts.py cse_bake.py <bake_only.py>
# where bake_only.py sets BAKE = ["alarm_unit"].
#
# One mesh, one material, one PNG (512 px): base colour of every material x ambient occlusion x
# a little grime, with a gloss mask in alpha (metal shiny, plastic dull) for the game's legacy
# Specular shader. The OBJ gets UVs and `map_Kd <name>.png`; the mod loads it like the others.
import random

import numpy as np

BAKE_SIZE = 512
AO_DISTANCE = 0.015     # m: contact shadows in seams and corners, not across the whole part
AO_STRENGTH = 0.8
GRIME = 0.08            # darkest blotch, fraction of the colour
BAKE_ROOT = r"C:\Users\User\source\CarSystemsExtended"


def _geo(col):
    return [o for o in col.objects if o.type in ('MESH', 'CURVE', 'FONT')
            and not o.get("cse_marker") and not o.get("cse_cutter")]


def _merged(scene, col, name):
    """Copy of every visible object of `col` with modifiers applied, as one mesh object."""
    bake_col = fresh_collection(scene, "CSE Bake " + name)
    deps = bpy.context.evaluated_depsgraph_get()
    bm = bmesh.new()
    materials = []
    for ob in _geo(col):
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
        me.transform(ob.matrix_world)
        remap = []
        mats = list(me.materials) or [None]
        for m in mats:
            if m is None:
                m = material("cse_bake_default", (0.3, 0.3, 0.3))
            if m not in materials:
                materials.append(m)
            remap.append(materials.index(m))
        before = len(bm.faces)
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        for f in bm.faces[before:]:
            f.material_index = remap[min(f.material_index, len(remap) - 1)]
        bpy.data.meshes.remove(me)
    out = bpy.data.meshes.new("cse_bake_" + name)
    bm.to_mesh(out)
    bm.free()
    for m in materials:
        out.materials.append(m)
    ob = bpy.data.objects.new("cse_bake_" + name, out)
    bake_col.objects.link(ob)
    return bake_col, ob


def _context(ob):
    return bpy.context.temp_override(object=ob, active_object=ob, selected_objects=[ob],
                                     selected_editable_objects=[ob], edit_object=ob)


def _unwrap(ob):
    # one UV map only: primitives bring their own, and both unwrapping and baking use the active one
    for layer in list(ob.data.uv_layers):
        ob.data.uv_layers.remove(layer)
    uv = ob.data.uv_layers.new(name="bake")
    ob.data.uv_layers.active = uv
    uv.active_render = True
    for o in bpy.context.view_layer.objects:
        o.select_set(o == ob)
    bpy.context.view_layer.objects.active = ob
    with _context(ob):
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.015, area_weight=0.0)
        bpy.ops.object.mode_set(mode='OBJECT')


def _image(name, non_color):
    img = bpy.data.images.get(name)
    if img is not None:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(name, BAKE_SIZE, BAKE_SIZE, alpha=False)
    if non_color:
        img.colorspace_settings.name = 'Non-Color'
    return img


def _target(ob, img):
    """Make `img` the bake target of every material on the object."""
    for m in ob.data.materials:
        nt = m.node_tree
        node = nt.nodes.get("cse_bake_target") or nt.nodes.new("ShaderNodeTexImage")
        node.name = "cse_bake_target"
        node.image = img
        nt.nodes.active = node


def _bake(ob, kind, img, **kw):
    _target(ob, img)
    with _context(ob):
        bpy.ops.object.bake(type=kind, margin=6, use_clear=True, **kw)


def _px(img):
    a = np.empty(BAKE_SIZE * BAKE_SIZE * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(BAKE_SIZE, BAKE_SIZE, 4)


def _grime(seed):
    """Soft blotches: random values on a coarse grid, smoothly upsampled."""
    rnd = np.random.RandomState(seed)
    out = np.ones((BAKE_SIZE, BAKE_SIZE), dtype=np.float32)
    for cells, weight in ((6, 0.6), (18, 0.4)):
        g = rnd.rand(cells + 1, cells + 1).astype(np.float32)
        x = np.linspace(0, cells, BAKE_SIZE)
        i = np.minimum(x.astype(int), cells - 1)
        t = x - i
        t = t * t * (3 - 2 * t)
        rows = g[i] * (1 - t)[:, None] + g[i + 1] * t[:, None]
        full = rows[:, i] * (1 - t)[None, :] + rows[:, i + 1] * t[None, :]
        out -= GRIME * weight * full
    return out


def bake_textured(scene, col, name):
    src_mats = {}
    bake_col, ob = _merged(scene, col, name)
    # bake on copies so the source materials (and the flat export) stay as they are
    for i, m in enumerate(ob.data.materials):
        copy = m.copy()
        src_mats[copy.name] = m
        ob.data.materials[i] = copy
    _unwrap(ob)

    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 24
    scene.render.bake.use_selected_to_active = False
    world = scene.world or bpy.data.worlds.new("cse_world")
    scene.world = world
    world.light_settings.distance = AO_DISTANCE

    # only the merged copy may be seen by the bake: the source objects sit exactly on top of it
    # and would black out the whole AO
    hidden = []
    for c in scene.collection.children:
        if c != bake_col and not c.hide_render:
            c.hide_render = True
            hidden.append(c)

    # base colour as emission: a diffuse-colour bake gives metals (no diffuse) as black
    for m in ob.data.materials:
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf is not None:
            bsdf.inputs["Emission Color"].default_value = bsdf.inputs["Base Color"].default_value[:]
            bsdf.inputs["Emission Strength"].default_value = 1.0
    color = _image("cse_bake_color_" + name, False)
    _bake(ob, 'EMIT', color)
    ao = _image("cse_bake_ao_" + name, True)
    _bake(ob, 'AO', ao)

    # gloss mask: each material glows with its shininess, baked as emission
    for m in ob.data.materials:
        src = src_mats[m.name]
        gloss = 1.0 if src.metallic > 0.5 else max(0.0, 0.6 - src.roughness) * 0.6
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf is not None:
            bsdf.inputs["Emission Color"].default_value = (gloss, gloss, gloss, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 1.0
    gl = _image("cse_bake_gloss_" + name, True)
    _bake(ob, 'EMIT', gl)

    for c in hidden:
        c.hide_render = False

    c = _px(color)
    shade = 1.0 - AO_STRENGTH * (1.0 - _px(ao)[..., 0])
    dirt = _grime(sum(map(ord, name)))
    final = np.empty_like(c)
    final[..., :3] = np.clip(c[..., :3] * (shade * dirt)[..., None], 0, 1)
    final[..., 3] = np.clip(_px(gl)[..., 0], 0, 1)

    tex = bpy.data.images.get(name + ".png")
    if tex is not None:
        bpy.data.images.remove(tex)
    tex = bpy.data.images.new(name + ".png", BAKE_SIZE, BAKE_SIZE, alpha=True)
    tex.pixels.foreach_set(final.ravel())
    path = BAKE_ROOT + r"\CarSystemsExtended\Assets\models" + "\\" + name + ".png"
    tex.alpha_mode = 'CHANNEL_PACKED'  # alpha is the gloss mask, colour is not premultiplied
    tex.filepath_raw = path
    tex.file_format = 'PNG'
    tex.save()

    # one textured material for the export and the preview (alpha is gloss, not opacity)
    mat = bpy.data.materials.get("cse_tex_" + name) or bpy.data.materials.new("cse_tex_" + name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    img_node = nt.nodes.get("cse_tex") or nt.nodes.new("ShaderNodeTexImage")
    img_node.name = "cse_tex"
    img_node.image = tex
    nt.links.new(img_node.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.55
    ob.data.materials.clear()
    ob.data.materials.append(mat)
    for m in list(src_mats.keys()):
        bpy.data.materials.remove(bpy.data.materials[m])

    stats = _export_textured(ob, [o for o in col.objects if o.get("cse_marker")], name)
    preview(scene, bake_col, BAKE_ROOT + r"\tools\blender\previews" + "\\" + name + "_tex.png",
            view=BAKE_VIEWS.get(name, (35, 0, 30)))
    stats["texture"] = path
    return stats


BAKE_VIEWS = {"alarm_unit": (35, 0, 210)}


def _export_textured(ob, markers, name):
    me = ob.data
    me.calc_loop_triangles()
    uv = me.uv_layers["bake"].data
    cn = me.corner_normals
    path = BAKE_ROOT + r"\CarSystemsExtended\Assets\models" + "\\" + name
    with open(path + ".mtl", "w", encoding="utf-8") as f:
        f.write("newmtl {0}\nKd 1.0000 1.0000 1.0000\nd 1.000\nNs 45.0\nPm 0.000\nmap_Kd {0}.png\n".format(name))
    pos_i, nrm_i, uv_i = {}, {}, {}
    pos_l, nrm_l, uv_l, faces = [], [], [], ["usemtl {}\n".format(name)]

    def idx(table, lines, fmt, vec, digits):
        key = tuple(round(c, digits) for c in vec)
        i = table.get(key)
        if i is None:
            lines.append(fmt.format(*key))
            i = table[key] = len(lines)
        return i

    for t in me.loop_triangles:
        ids = []
        for k in range(3):
            li = t.loops[k]
            p = me.vertices[me.loops[li].vertex_index].co
            ids.append((idx(pos_i, pos_l, "v {:.5f} {:.5f} {:.5f}\n", _to_part(p), 5),
                        idx(uv_i, uv_l, "vt {:.5f} {:.5f}\n", tuple(uv[li].uv), 5),
                        idx(nrm_i, nrm_l, "vn {:.3f} {:.3f} {:.3f}\n", _to_part(cn[li].vector.normalized()), 3)))
        # reversed winding, as in export(): the axis change is a reflection
        faces.append("f {}/{}/{} {}/{}/{} {}/{}/{}\n".format(*ids[2], *ids[1], *ids[0]))
    with open(path + ".obj", "w", encoding="utf-8") as f:
        f.write("# Car Systems Extended part model '{}', part space (Unity, left-handed), baked texture\n".format(name))
        f.write("mtllib {}.mtl\n".format(name))
        for mk in markers:
            f.write("#marker {} {:.5f} {:.5f} {:.5f}\n".format(mk.name.split(".")[0], *_to_part(mk.matrix_world.translation)))
        f.writelines(pos_l)
        f.writelines(uv_l)
        f.writelines(nrm_l)
        f.writelines(faces)
    return {"triangles": len(me.loop_triangles), "file": path + ".obj"}


result = {}
for _name in BAKE:  # noqa: F821 - set by a snippet sent before this file
    _col = bpy.data.collections.get("CSE " + _name.replace("_", " "))
    result[_name] = bake_textured(scene, _col, _name) if _col else "collection not found"
