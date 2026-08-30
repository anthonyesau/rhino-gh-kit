"""@component
{
  "name":        "Sci-Fi Text Extrude PY",
  "nickname":    "SciFiX",
  "description": "Extrudes a sci-fi quote along a randomly wandering path - pick one of 100 book, film, and show lines by Index, then Seed drives both the path's shape and a random font choice. Each letter is displaced onto the path and extruded along the path's local direction there, so the phrase reads like it was carved along a wandering ribbon. Unwired inputs fall back to phrase 0, seed 0, and a height of 2.",
  "icon":        "icons/sci-fi-text-extrude-py.svg",
  "category":    "Surface",
  "subcategory": "Text",

  "inputs": [
    { "name": "Index", "nickname": "i", "type": "int", "access": "item",
      "description": "Which phrase to use, 0-99 (wraps around; default 0 when unwired)." },
    { "name": "Seed", "nickname": "S", "type": "int", "access": "item",
      "description": "Random seed - drives the wandering path shape and the font choice (default 0)." },
    { "name": "Height", "nickname": "H", "type": "double", "access": "item",
      "description": "Text height, also sets the extrusion depth and path wander (default 2.0)." },
    { "name": "BasePlane", "nickname": "P", "type": "Plane", "access": "item",
      "description": "Plane the phrase is authored on before it is bent onto the path (default world XY)." }
  ],

  "outputs": [
    { "name": "Text", "nickname": "T", "type": "string", "access": "item",
      "description": "The selected phrase." },
    { "name": "Font", "nickname": "F", "type": "string", "access": "item",
      "description": "The randomly chosen font name." },
    { "name": "Path", "nickname": "C", "type": "Curve", "access": "item",
      "description": "The randomly generated path curve the letters ride on." },
    { "name": "Geometry", "nickname": "G", "type": "Brep", "access": "list",
      "description": "One extruded solid per letter." }
  ]
}
"""
import random
import Rhino.Geometry as rg

PHRASES = [
    "I'll be back.",
    "May the Force be with you.",
    "Live long and prosper.",
    "Beam me up, Scotty.",
    "Resistance is futile.",
    "There is no spoon.",
    "No, I am your father.",
    "Roads? Where we're going we don't need roads.",
    "Great Scott!",
    "I'm sorry, I'm afraid I can't do that.",
    "Hasta la vista, baby.",
    "Make it so.",
    "Fly, you fools!",
    "This is the way.",
    "Space: the final frontier.",
    "It's a trap!",
    "May the odds be ever in your favor.",
    "The cake is a lie.",
    "Pray I don't alter it any further.",
    "Do or do not, there is no try.",
    "Never tell me the odds.",
    "It's alive! It's alive!",
    "I find your lack of faith disturbing.",
    "So say we all.",
    "Danger, Will Robinson.",
    "Klaatu barada nikto.",
    "The truth is out there.",
    "Take me to your leader.",
    "Ground Control to Major Tom.",
    "In space, no one can hear you scream.",
    "Game over, man. Game over.",
    "I have a bad feeling about this.",
    "Help me, Obi-Wan Kenobi.",
    "These are not the droids you're looking for.",
    "Punch it.",
    "I am Groot.",
    "Wubba lubba dub dub.",
    "To infinity and beyond.",
    "Life finds a way.",
    "Welcome to Jurassic Park.",
    "Clever girl.",
    "Must go faster.",
    "Are you my mummy?",
    "Exterminate!",
    "Bad wolf.",
    "Allons-y!",
    "Bigger on the inside.",
    "Time is relative.",
    "Gentlemen, we can rebuild him.",
    "We have the technology.",
    "Open the pod bay doors, HAL.",
    "I'm afraid I can't let you do that.",
    "By your command.",
    "All this has happened before.",
    "The spice must flow.",
    "He who controls the spice controls the universe.",
    "Fear is the mind-killer.",
    "I must not fear.",
    "The sleeper has awakened.",
    "Long live the fighters.",
    "So it goes.",
    "Ender wept.",
    "The enemy's gate is down.",
    "In the beginning, there was nothing.",
    "The answer is forty-two.",
    "Don't panic.",
    "So long, and thanks for all the fish.",
    "Mostly harmless.",
    "Time is a flat circle.",
    "We are all made of star stuff.",
    "The stars, they whisper.",
    "One small step for man.",
    "Houston, we have a problem.",
    "Failure is not an option.",
    "In space no plan survives first contact.",
    "Bring balance to the Force.",
    "The Force is strong with this one.",
    "Wonderful things.",
    "Welcome to the future.",
    "The future is now.",
    "This is Cyberdyne Systems.",
    "Judgment day is coming.",
    "Come with me if you want to live.",
    "I need your clothes, your boots, and your motorcycle.",
    "Skynet is aware.",
    "Robots do not lie.",
    "The three laws of robotics.",
    "I, for one, welcome our new overlords.",
    "Are you alive?",
    "We are the Borg.",
    "You will be assimilated.",
    "Engage.",
    "Set phasers to stun.",
    "Highly illogical.",
    "Fascinating.",
    "The needs of the many outweigh the few.",
    "Klingons do not take prisoners.",
    "Boldly go where no one has gone before.",
    "Genesis is life from lifelessness.",
    "There is no gene for the human spirit.",
]

FONTS = [
    "Arial",
    "Arial Black",
    "Verdana",
    "Tahoma",
    "Trebuchet MS",
    "Segoe UI",
    "Segoe UI Black",
    "Consolas",
    "Courier New",
    "Lucida Console",
    "Impact",
    "Bahnschrift",
    "Century Gothic",
    "Franklin Gothic Medium",
    "Eurostile",
    "Bank Gothic",
    "Orbitron",
    "Agency FB",
    "Copperplate Gothic Bold",
    "OCR A Extended",
]

idx = int(Index) % len(PHRASES) if Index else 0
seed = int(Seed) if Seed else 0
h = float(Height) if Height and Height > 0 else 2.0
plane = BasePlane if BasePlane else rg.Plane.WorldXY

rnd = random.Random(seed)
phrase = PHRASES[idx]
font = rnd.choice(FONTS)

tol = 0.01 * h

# CreateTextOutlines returns one FLAT array of closed outline curves (spaces
# contribute nothing) - not per-glyph lists. Rebuild the glyph units by
# containment: sort by enclosed area so outer boundaries come first, then
# attach each curve to the unit whose outer loop contains it (a letter's
# counters become holes); anything not contained starts its own unit (a
# detached dot of an i, a quote mark).
outline_curves = rg.Curve.CreateTextOutlines(phrase, font, h, 0, True, plane, 1.0, tol)
outline_curves = [c for c in outline_curves if c] if outline_curves else []


def loop_area(c):
    amp = rg.AreaMassProperties.Compute(c)
    return amp.Area if amp else 0.0


glyphs = []
for c in sorted(outline_curves, key=loop_area, reverse=True):
    holder = None
    for unit in glyphs:
        if unit[0].Contains(c.PointAtStart, plane, tol) == rg.PointContainment.Inside:
            holder = unit
            break
    if holder is not None:
        holder.append(c)
    else:
        glyphs.append([c])

phrase_box = rg.BoundingBox.Empty
for loops in glyphs:
    for c in loops:
        phrase_box.Union(c.GetBoundingBox(True))

width = max(phrase_box.Diagonal.X, 1e-6)
wander = h * 1.5
n_ctrl = 8

path_pts = []
for i in range(n_ctrl):
    t = i / float(n_ctrl - 1)
    x = phrase_box.Min.X + t * width
    y = (rnd.random() - 0.5) * 2.0 * wander
    z = (rnd.random() - 0.5) * 2.0 * wander
    path_pts.append(plane.PointAt(x, y, z))

path = rg.Curve.CreateInterpolatedCurve(path_pts, 3)


def extrude_loops(loops, direction, tolerance):
    breps = []
    bottom = rg.Brep.CreatePlanarBreps(loops, tolerance)
    if bottom:
        breps.extend(bottom)

    top_loops = []
    for c in loops:
        dup = c.DuplicateCurve()
        dup.Translate(direction)
        top_loops.append(dup)
    top = rg.Brep.CreatePlanarBreps(top_loops, tolerance)
    if top:
        breps.extend(top)

    for c in loops:
        wall = rg.Surface.CreateExtrusion(c, direction)
        if wall:
            breps.append(wall.ToBrep())

    if not breps:
        return []
    joined = rg.Brep.JoinBreps(breps, tolerance)
    return list(joined) if joined else []


depth = h * 0.6
solids = []
for loops in glyphs:
    glyph_box = rg.BoundingBox.Empty
    for c in loops:
        glyph_box.Union(c.GetBoundingBox(True))
    cx = glyph_box.Center.X

    base_pt = plane.PointAt(cx, 0.0, 0.0)
    ok, t = path.ClosestPoint(base_pt)
    if not ok:
        continue

    tangent = path.TangentAt(t)
    if not tangent.IsValid or tangent.Length < 1e-9:
        tangent = plane.ZAxis
    tangent.Unitize()

    shift = path.PointAt(t) - base_pt
    moved_loops = []
    for c in loops:
        dup = c.DuplicateCurve()
        dup.Translate(shift)
        moved_loops.append(dup)

    solids.extend(extrude_loops(moved_loops, tangent * depth, tol))

Text = phrase
Font = font
Path = path
Geometry = solids
