// graph.js: the dependency graph view, drawn with d3.
//
// Two levels: one node per module, or one node per member with each module's members drawn as a cluster
// inside a shaded outline. Edges are Uses (solid), Implements (dashed, hollow arrow) and Events (dotted).
// Clicking a node selects it through window.vbadoc, which shows its source; the graph then highlights what
// the selection depends on and what depends on it, directly or transitively.
//
// The starting layout is computed in one go before anything is drawn. d3-force starts from fixed positions,
// so the same project and settings always give the same first picture. After that the force simulation is
// kept: dragging a node (or a module's outline) wakes it up so the rest of the graph rearranges around it,
// and the settings panel changes its forces live.
(function () {
  "use strict";

  var vb = window.vbadoc;
  var data = vb.data;

  // ---- What things look like ----

  // Colour carries three groups (validated as a palette); shape tells the five module kinds apart.
  var MODULE_STYLES = {
    standard: { group: "code", shape: d3.symbolSquare, label: "Standard module" },
    "class": { group: "class", shape: d3.symbolCircle, label: "Class" },
    "interface": { group: "class", shape: d3.symbolCircle, label: "Interface", hollow: true },
    form: { group: "host", shape: d3.symbolDiamond, label: "UserForm" },
    document: { group: "host", shape: d3.symbolTriangle, label: "Document module" }
  };

  var MEMBER_CATEGORIES = [
    { id: "procedure", label: "Procedures", kinds: ["sub", "function"], shape: d3.symbolCircle },
    { id: "property", label: "Properties", kinds: ["propertyGet", "propertyLet", "propertySet"], shape: d3.symbolDiamond },
    { id: "data", label: "Variables and constants", kinds: ["variable", "constant"], shape: d3.symbolSquare },
    { id: "type", label: "Enums and types", kinds: ["enum", "type"], shape: d3.symbolTriangle },
    { id: "enumValue", label: "Enum values", kinds: ["enumMember"], shape: d3.symbolTriangle },
    { id: "event", label: "Events", kinds: ["event"], shape: d3.symbolStar },
    { id: "declare", label: "Declares", kinds: ["declare"], shape: d3.symbolCross }
  ];

  var EDGE_KINDS = [
    { id: "uses", label: "Uses" },
    { id: "implements", label: "Implements" },
    { id: "events", label: "Events" }
  ];

  var KIND_LABELS = {
    sub: "Sub", "function": "Function", propertyGet: "Property Get", propertyLet: "Property Let",
    propertySet: "Property Set", variable: "Variable", constant: "Const", "enum": "Enum", enumMember: "Enum value",
    type: "Type", event: "Event", declare: "Declare"
  };

  var categoryOfKind = {};
  MEMBER_CATEGORIES.forEach(function (c) { c.kinds.forEach(function (k) { categoryOfKind[k] = c; }); });

  function moduleStyle(module) { return MODULE_STYLES[module.isInterface ? "interface" : module.kind] || MODULE_STYLES["class"]; }

  // ---- Settings (remembered per browser; storage can fail on file://) ----

  function loadSetting(key, fallback) { try { return localStorage.getItem(key) || fallback; } catch (e) { return fallback; } }
  function saveSetting(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* not remembered */ } }

  var state = {
    level: loadSetting("vbadoc-graph-level", "modules") === "members" ? "members" : "modules",
    reach: loadSetting("vbadoc-graph-reach", "direct") === "transitive" ? "transitive" : "direct",
    focus: false,
    hiddenCategories: new Set(),
    unreferencedOnly: false
  };

  // Everything the settings panel controls. `layout` marks settings that change the forces, so changing
  // them wakes the simulation up rather than just restyling.
  var DEFAULTS = {
    labels: "always",      // member names: "always", "zoom" (when zoomed in) or "selection"
    labelSize: 10.5,       // member names, px; module names are 1.5px bigger
    nodeSize: 1,
    edgeOpacity: 1,
    curvedEdges: true,
    bundleEdges: false,
    showHulls: true,
    live: true,            // rearrange the rest of the graph while dragging
    pinDragged: false,     // dragged nodes stay where they're dropped
    linkStrength: 1,
    memberSpacing: 12,     // px between neighbouring member names
    moduleSpacing: 40,     // px between module outlines
    cohesion: 1,
    linkDistance: 150,     // module level
    repulsion: 1           // module level
  };

  var CONTROLS = [
    { section: "Names" },
    { key: "labels", type: "select", label: "Member names",
      options: [["always", "Always"], ["zoom", "When zoomed in"], ["selection", "Selection and neighbours only"]] },
    { key: "labelSize", type: "range", label: "Text size", min: 8, max: 18, step: 0.5, unit: "px", layout: true },
    { section: "Look" },
    { key: "nodeSize", type: "range", label: "Node size", min: 0.5, max: 2.5, step: 0.1, unit: "×", layout: true },
    { key: "edgeOpacity", type: "range", label: "Edge opacity", min: 0.1, max: 1, step: 0.05 },
    { key: "curvedEdges", type: "checkbox", label: "Curved edges" },
    { key: "bundleEdges", type: "checkbox", label: "Bundle edges between modules", note: "member level" },
    { key: "showHulls", type: "checkbox", label: "Module outlines", note: "member level" },
    { section: "Dragging" },
    { key: "live", type: "checkbox", label: "Rearrange while dragging" },
    { key: "pinDragged", type: "checkbox", label: "Keep dragged nodes where dropped", note: "double-click a node to release it" },
    { section: "Layout" },
    { key: "linkStrength", type: "range", label: "Edge pull", min: 0, max: 3, step: 0.1, unit: "×", layout: true },
    { key: "memberSpacing", type: "range", label: "Space between members", min: 0, max: 40, step: 1, unit: "px", layout: true, note: "member level" },
    { key: "moduleSpacing", type: "range", label: "Space between modules", min: 0, max: 200, step: 5, unit: "px", layout: true, note: "member level" },
    { key: "cohesion", type: "range", label: "Module compactness", min: 0.1, max: 3, step: 0.1, unit: "×", layout: true, note: "member level" },
    { key: "linkDistance", type: "range", label: "Edge length", min: 40, max: 400, step: 10, unit: "px", layout: true, note: "module level" },
    { key: "repulsion", type: "range", label: "Repulsion", min: 0.1, max: 3, step: 0.1, unit: "×", layout: true, note: "module level" }
  ];

  var settings = loadSettings();

  function loadSettings() {
    var stored = {};
    try { stored = JSON.parse(loadSetting("vbadoc-graph-settings", "{}")) || {}; } catch (e) { stored = {}; }
    var result = {};
    Object.keys(DEFAULTS).forEach(function (key) {
      result[key] = typeof stored[key] === typeof DEFAULTS[key] ? stored[key] : DEFAULTS[key];
    });
    return result;
  }

  // ---- Elements ----

  var view = document.getElementById("view-graph");
  var svg = d3.select("#graph-svg");
  var defs = svg.append("defs");
  var viewport = svg.append("g").attr("class", "viewport");
  var hullLayer = viewport.append("g").attr("class", "hulls");
  var edgeLayer = viewport.append("g").attr("class", "edges");
  var nodeLayer = viewport.append("g").attr("class", "nodes");
  var tooltip = document.getElementById("graph-tooltip");
  var message = document.getElementById("graph-message");
  var focusButton = document.getElementById("graph-focus");
  var filtersBox = document.getElementById("graph-filters");

  EDGE_KINDS.forEach(function (kind) {
    defs.append("marker")
      .attr("id", "arrow-" + kind.id)
      .attr("viewBox", "0 -5 10 10")
      .attr("refX", 9)
      .attr("markerWidth", 7)
      .attr("markerHeight", 7)
      .attr("markerUnits", "userSpaceOnUse")
      .attr("orient", "auto")
      .append("path")
      .attr("class", "arrow-" + kind.id)
      .attr("d", "M0,-4L9,0L0,4Z");
  });

  // ---- Building the graph for the current level and filters ----

  var memberCountByModule = new Map();
  data.members.forEach(function (m) { memberCountByModule.set(m.module, (memberCountByModule.get(m.module) || 0) + 1); });

  function buildFullGraph() {
    var nodes = [];
    var edges;
    var size = settings.nodeSize;
    if (state.level === "modules") {
      data.modules.forEach(function (m) {
        var style = moduleStyle(m);
        nodes.push({
          id: m.id, module: m.id, label: m.id, group: style.group, shape: style.shape, hollow: !!style.hollow,
          r: size * Math.min(20, 7 + 1.6 * Math.sqrt(memberCountByModule.get(m.id) || 0)), title: style.label, isModule: true
        });
      });
      edges = data.moduleEdges;
    } else {
      data.members.forEach(function (m) {
        var category = categoryOfKind[m.kind];
        if (state.hiddenCategories.has(category.id)) return;
        if (state.unreferencedOnly && !m.unreferenced) return;
        var module = vb.modulesById.get(m.module);
        nodes.push({
          id: m.id, module: m.module, label: m.name, group: moduleStyle(module).group, shape: category.shape,
          r: size * (m.kind === "enumMember" ? 4 : 5.5), title: (KIND_LABELS[m.kind] || m.kind) + " in " + m.module,
          unreferenced: m.unreferenced, minor: true
        });
      });
      // Each module also has a node for references to the module itself (e.g. "As Order", "Sheet1.Range").
      data.modules.forEach(function (m) {
        nodes.push({
          id: m.id, module: m.id, label: m.id, group: moduleStyle(m).group, shape: d3.symbolCircle, r: size * 7,
          title: moduleStyle(m).label + " (the module itself)", isModuleNode: true, minor: true
        });
      });
      edges = data.memberEdges;
    }

    var ids = new Set(nodes.map(function (n) { return n.id; }));
    edges = edges.filter(function (e) { return ids.has(e.from) && ids.has(e.to); });
    var used = new Set();
    edges.forEach(function (e) { used.add(e.from); used.add(e.to); });
    nodes = nodes.filter(function (n) { return !n.isModuleNode || used.has(n.id); }); // only module nodes that are referenced
    return indexGraph(nodes, edges);
  }

  function indexGraph(nodes, edges) {
    var byId = new Map();
    nodes.forEach(function (n) { byId.set(n.id, n); });
    var outgoing = new Map();
    var incoming = new Map();
    nodes.forEach(function (n) { outgoing.set(n.id, []); incoming.set(n.id, []); });
    edges = edges.map(function (e) {
      var edge = { from: e.from, to: e.to, kind: e.kind, count: e.count };
      outgoing.get(e.from).push(edge);
      incoming.get(e.to).push(edge);
      return edge;
    });
    edges.forEach(function (e) { if (e.from === e.to) byId.get(e.from).selfLoop = true; });
    return { nodes: nodes, edges: edges, byId: byId, outgoing: outgoing, incoming: incoming };
  }

  // The node ids that stand for the page's selection at this level. At module level a member stands for its
  // module; at member level a module stands for all of its members.
  function selectedIds(graph) {
    var selection = vb.selection();
    if (!selection) return [];
    if (state.level === "modules") {
      var member = vb.membersById.get(selection);
      var moduleId = member ? member.module : selection;
      return graph.byId.has(moduleId) ? [moduleId] : [];
    }
    if (vb.membersById.has(selection)) return graph.byId.has(selection) ? [selection] : [];
    return graph.nodes.filter(function (n) { return n.module === selection; }).map(function (n) { return n.id; });
  }

  // What to highlight: the selection, what it depends on (downstream) and what depends on it (upstream).
  // Direct goes one step each way; transitive follows the whole chain.
  function reach(graph, start, transitive) {
    var selected = new Set(start);
    function walk(edgesOf, endOf) {
      var found = new Set();
      var queue = start.slice();
      while (queue.length > 0) {
        var id = queue.shift();
        edgesOf.get(id).forEach(function (e) {
          var next = endOf(e);
          if (selected.has(next) || found.has(next)) return;
          found.add(next);
          if (transitive) queue.push(next);
        });
      }
      return found;
    }
    var downstream = walk(graph.outgoing, function (e) { return e.to; });
    var upstream = walk(graph.incoming, function (e) { return e.from; });

    var nodes = new Set(start);
    downstream.forEach(function (id) { nodes.add(id); });
    upstream.forEach(function (id) { nodes.add(id); });

    // Direct: the edges touching the selection. Transitive: every edge on a chain from or to it.
    var fromSide = transitive ? new Set(Array.from(selected).concat(Array.from(downstream))) : selected;
    var toSide = transitive ? new Set(Array.from(selected).concat(Array.from(upstream))) : selected;
    var edges = new Set(graph.edges.filter(function (e) { return fromSide.has(e.from) || toSide.has(e.to); }));
    return { selected: selected, nodes: nodes, edges: edges };
  }

  // ---- Layout ----
  //
  // One simulation per drawn graph. Its forces are set from the settings by configureForces(), both for the
  // starting layout and whenever a layout setting changes.

  var simulation = null;

  function createSimulation(graph) {
    if (simulation) simulation.stop();
    simulation = d3.forceSimulation(graph.nodes).stop();
    simulation.on("tick", updatePositions);
    // When the graph comes to rest after dragging or a settings change, tidy up as the starting layout does.
    simulation.on("end", function () { removeOverlaps(graph); updatePositions(); });
    configureForces(graph);
  }

  // Forces can leave small overlaps where they balance; remove them outright (member level only).
  function removeOverlaps(graph) {
    if (state.level !== "members") return;
    var groupList = Array.from(d3.group(graph.nodes, function (n) { return n.module; }).values());
    for (var pass = 0; pass < 100 && separateBoxes(groupList, 1, true); pass++) { /* until nothing overlaps */ }
    for (pass = 0; pass < 200 && separateModules(groupList, 1, true); pass++) { /* likewise for modules */ }
  }

  function configureForces(graph) {
    var links = graph.edges.filter(function (e) { return e.from !== e.to; }).map(function (e) { return { source: e.from, target: e.to }; });
    graph.nodes.forEach(setBox);

    if (state.level === "modules") {
      // Labels sit to the right of each node, so leave room for them when keeping nodes apart.
      simulation
        .force("link", d3.forceLink(links).id(function (d) { return d.id; }).distance(settings.linkDistance).strength(0.3 * settings.linkStrength))
        .force("charge", d3.forceManyBody().strength(-900 * settings.repulsion))
        .force("collide", d3.forceCollide(function (d) { return Math.max(d.r + 20, (d.r + labelWidth(d)) / 2 + 12); }))
        .force("x", d3.forceX(0).strength(0.05))
        .force("y", d3.forceY(0).strength(0.05));
      return;
    }

    // Member level. Each member takes up a rectangle (its mark plus its name, which sits to the right), and
    // rectangles in the same module are kept apart so names never overlap. Members are pulled towards their
    // module's middle, modules are kept apart as whole rectangles, and edges pull linked members together:
    // strongly inside a module, gently between modules, which draws related modules near each other and
    // moves linked members to the side facing the module they're linked with.
    var groupList = Array.from(d3.group(graph.nodes, function (n) { return n.module; }).values());
    simulation
      .force("link", d3.forceLink(links).id(function (d) { return d.id; })
        .distance(function (l) { return l.source.module === l.target.module ? 30 : 80; })
        .strength(function (l) { return (l.source.module === l.target.module ? 0.05 : 0.003) * settings.linkStrength; }))
      .force("charge", null)
      .force("collide", null)
      .force("cohesion", cohesionForce(groupList))
      .force("boxes", function () { separateBoxes(groupList, 0.7, false); })
      .force("modules", function () { separateModules(groupList, 0.5, false); })
      .force("x", d3.forceX(0).strength(0.01))
      .force("y", d3.forceY(0).strength(0.01));
  }

  // The starting layout: module places first, then members around them, then the simulation settles it all.
  function initialLayout(graph) {
    if (state.level === "members") {
      var groups = d3.group(graph.nodes, function (n) { return n.module; });
      var estimates = new Map();
      groups.forEach(function (members, id) {
        var area = d3.sum(members, function (n) { setBox(n); return (n.x1 - n.x0) * (n.y1 - n.y0); });
        estimates.set(id, Math.sqrt(area / Math.PI / 0.6)); // rectangles fill about 60% of the circle
      });
      var centers = clusterCenters(graph, estimates);
      var placedInModule = new Map();
      graph.nodes.forEach(function (n) {
        var c = centers.get(n.module);
        var k = placedInModule.get(n.module) || 0;
        placedInModule.set(n.module, k + 1);
        var angle = k * 2.39996; // golden angle: an even, fixed spiral, wider than tall like the rectangles
        var radius = 12 * Math.sqrt(k);
        n.x = c.x + 2 * radius * Math.cos(angle);
        n.y = c.y + radius * Math.sin(angle);
      });
    } else {
      graph.nodes.forEach(function (n) { delete n.x; delete n.y; }); // d3's own fixed starting spiral
    }
    graph.nodes.forEach(function (n) { n.vx = 0; n.vy = 0; n.fx = null; n.fy = null; n.pinned = false; });
    createSimulation(graph);
    simulation.alpha(1);
    for (var i = 0; i < 300; i++) simulation.tick();
    removeOverlaps(graph);
    simulation.alpha(0);
  }

  // Wakes the simulation after a layout setting changed, so the graph visibly moves to its new shape.
  function relayout() {
    if (!graph) return;
    configureForces(graph);
    simulation.alpha(0.6).restart();
  }

  // Label widths are measured in the font the labels are drawn in, so the layout leaves just enough room.
  var measure = document.createElement("canvas").getContext("2d");
  var fontFamily = getComputedStyle(document.documentElement).getPropertyValue("--font-ui").trim() || "sans-serif";
  function labelWidth(n) {
    var size = n.minor ? settings.labelSize : settings.labelSize + 1.5;
    if (!measure) return n.label.length * size * 0.6;
    var font = size + "px " + fontFamily;
    if (measure.font !== font) measure.font = font;
    return measure.measureText(n.label).width;
  }

  // A member's rectangle relative to its position: the mark, the 5px gap, the name, and some breathing room.
  function setBox(n) {
    var padX = 4 + settings.memberSpacing / 2;
    var height = settings.labelSize * 1.3 + settings.memberSpacing;
    n.x0 = -n.r - padX - (n.selfLoop ? 4 * n.r : 0); // a recursion loop is drawn on the left
    n.x1 = n.r + 5 + labelWidth(n) + padX;
    n.y0 = -Math.max(height, 2 * n.r + 4) / 2;
    n.y1 = -n.y0;
  }

  // Pulls each member towards the middle of its module. Rectangles are wide and short, so most pushes
  // between them are vertical; a weaker horizontal pull stops modules turning into tall single columns.
  function cohesionForce(groupList) {
    return function (alpha) {
      var kx = 0.02 * settings.cohesion * alpha;
      var ky = 0.3 * settings.cohesion * alpha;
      groupList.forEach(function (members) {
        var cx = d3.mean(members, function (n) { return n.x; });
        var cy = d3.mean(members, function (n) { return n.y; });
        members.forEach(function (n) {
          n.vx += (cx - n.x) * kx;
          n.vy += (cy - n.y) * ky;
        });
      });
    };
  }

  // Pushes overlapping rectangles in the same module apart, along whichever axis needs the smaller move.
  // d3's own collide force only knows circles, and a circle big enough for a long name wastes a lot of space.
  // As a force it nudges velocities; otherwise it moves nodes directly. Returns whether anything overlapped.
  function separateBoxes(groupList, strength, direct) {
    var overlapped = false;
    groupList.forEach(function (members) {
      for (var i = 0; i < members.length; i++) {
        var a = members[i];
        for (var j = i + 1; j < members.length; j++) {
          var b = members[j];
          var ox = Math.min(a.x + a.x1, b.x + b.x1) - Math.max(a.x + a.x0, b.x + b.x0);
          if (ox <= 0) continue;
          var oy = Math.min(a.y + a.y1, b.y + b.y1) - Math.max(a.y + a.y0, b.y + b.y0);
          if (oy <= 0) continue;
          overlapped = true;
          var key = direct ? "" : "v";
          if (ox < oy) {
            var dx = ((a.x + (a.x0 + a.x1) / 2) <= (b.x + (b.x0 + b.x1) / 2) ? -1 : 1) * (ox / 2 + 0.01) * strength;
            if (a.fx == null) a[key + "x"] += dx;
            if (b.fx == null) b[key + "x"] -= dx;
          } else {
            var dy = (a.y <= b.y ? -1 : 1) * (oy / 2 + 0.01) * strength;
            if (a.fy == null) a[key + "y"] += dy;
            if (b.fy == null) b[key + "y"] -= dy;
          }
        }
      }
    });
    return overlapped;
  }

  // A module's extent: the rectangles of all its members.
  function moduleBox(members) {
    var box = { x0: Infinity, y0: Infinity, x1: -Infinity, y1: -Infinity };
    members.forEach(function (n) {
      box.x0 = Math.min(box.x0, n.x + n.x0);
      box.x1 = Math.max(box.x1, n.x + n.x1);
      box.y0 = Math.min(box.y0, n.y + n.y0);
      box.y1 = Math.max(box.y1, n.y + n.y1);
    });
    return box;
  }

  // Keeps modules apart as whole rectangles (with the module name above), moving all of a module's members.
  // Like separateBoxes, it either nudges velocities or moves nodes directly, and returns whether anything
  // overlapped.
  function separateModules(groupList, strength, direct) {
    var overlapped = false;
    var gap = settings.moduleSpacing;
    var boxes = groupList.map(function (members) {
      var b = moduleBox(members);
      // A module holding a dragged or pinned node stays put and pushes the other module away instead;
      // otherwise its other members would be pushed away from the held one and the module torn apart.
      var held = members.some(function (n) { return n.fx != null; });
      return { members: members, held: held, x0: b.x0 - gap / 2, x1: b.x1 + gap / 2, y0: b.y0 - gap / 2 - 18, y1: b.y1 + gap / 2 };
    });
    for (var i = 0; i < boxes.length; i++) {
      var a = boxes[i];
      for (var j = i + 1; j < boxes.length; j++) {
        var b = boxes[j];
        var ox = Math.min(a.x1, b.x1) - Math.max(a.x0, b.x0);
        if (ox <= 0) continue;
        var oy = Math.min(a.y1, b.y1) - Math.max(a.y0, b.y0);
        if (oy <= 0) continue;
        if (a.held && b.held) continue;
        overlapped = true;
        var dx = 0, dy = 0;
        if (ox < oy) dx = ((a.x0 + a.x1) <= (b.x0 + b.x1) ? -1 : 1) * (ox / 2 + 0.01) * strength;
        else dy = ((a.y0 + a.y1) <= (b.y0 + b.y1) ? -1 : 1) * (oy / 2 + 0.01) * strength;
        if (a.held) move(b, -2 * dx, -2 * dy);
        else if (b.held) move(a, 2 * dx, 2 * dy);
        else { move(a, dx, dy); move(b, -dx, -dy); }
      }
    }
    function move(box, dx, dy) {
      box.x0 += dx; box.x1 += dx; box.y0 += dy; box.y1 += dy;
      box.members.forEach(function (n) {
        if (direct) { n.x += dx; n.y += dy; } else { n.vx += dx; n.vy += dy; }
      });
    }
    return overlapped;
  }

  // Places one circle per module (radius from `radii`), with linked modules drawn towards each other.
  function clusterCenters(graph, radii) {
    var clusters = Array.from(radii.keys()).map(function (id) { return { id: id, r: radii.get(id) }; });
    var seen = new Set();
    var links = [];
    graph.edges.forEach(function (e) {
      var a = graph.byId.get(e.from).module;
      var b = graph.byId.get(e.to).module;
      var key = a < b ? a + "\u0000" + b : b + "\u0000" + a;
      if (a !== b && !seen.has(key)) { seen.add(key); links.push({ source: a, target: b }); }
    });
    var clusterSimulation = d3.forceSimulation(clusters)
      .force("link", d3.forceLink(links).id(function (d) { return d.id; })
        .distance(function (l) { return l.source.r + l.target.r + 60; }).strength(0.2))
      .force("charge", d3.forceManyBody().strength(-150))
      .force("collide", d3.forceCollide(function (d) { return d.r + settings.moduleSpacing; }))
      .force("x", d3.forceX(0).strength(0.04))
      .force("y", d3.forceY(0).strength(0.04))
      .stop();
    for (var i = 0; i < 300; i++) clusterSimulation.tick();
    var centers = new Map();
    clusters.forEach(function (c) { centers.set(c.id, { x: c.x, y: c.y }); });
    return centers;
  }

  // ---- Drawing ----

  var graph = null;
  var zoomScale = 1;
  var zoom = d3.zoom().scaleExtent([0.05, 6]).on("zoom", function (event) {
    viewport.attr("transform", event.transform);
    zoomScale = event.transform.k;
    applyLabelMode();
    hideTooltip();
  });
  svg.call(zoom).on("dblclick.zoom", null);
  svg.on("click", function (event) { if (event.target === svg.node()) vb.select(null); });

  // Member names are shown for every node ("all-labels"), or only for the selection and its neighbours.
  function applyLabelMode() {
    svg.classed("all-labels", settings.labels === "always" || (settings.labels === "zoom" && zoomScale >= 1.3));
  }

  function applyStyleSettings() {
    var element = svg.node();
    element.style.setProperty("--graph-label-size", settings.labelSize + "px");
    element.style.setProperty("--graph-edge-opacity", settings.edgeOpacity);
    svg.classed("no-hulls", !settings.showHulls);
    applyLabelMode();
  }

  var KIND_ORDER = { uses: 0, implements: 1, events: 2 };

  // An edge. Curved, edges of different kinds between the same two nodes, and edges in opposite directions,
  // bend apart instead of overlapping. A node's edge to itself (recursion) is a loop.
  function edgePath(e) {
    var s = graph.byId.get(e.from);
    var t = graph.byId.get(e.to);
    if (s === t) {
      // On the left, because the name is on the right.
      var r = s.r;
      return "M" + (s.x - r * 0.7) + "," + (s.y - r * 0.7) +
        " C" + (s.x - r * 4) + "," + (s.y - r * 4) + " " + (s.x - r * 4.5) + "," + (s.y + r * 1.5) +
        " " + (s.x - r - 1) + "," + (s.y + r * 0.4);
    }
    if (settings.bundleEdges && graph.clusters && s.module !== t.module) return bundledPath(e, s, t);
    var dx = t.x - s.x;
    var dy = t.y - s.y;
    var bend = settings.curvedEdges ? 0.12 + 0.14 * KIND_ORDER[e.kind] : 0;
    var cx = (s.x + t.x) / 2 - dy * bend;
    var cy = (s.y + t.y) / 2 + dx * bend;
    var start = pointTowards(s, settings.curvedEdges ? cx : t.x, settings.curvedEdges ? cy : t.y, s.r + 1);
    var end = pointTowards(t, settings.curvedEdges ? cx : s.x, settings.curvedEdges ? cy : s.y, t.r + 2);
    if (!settings.curvedEdges) return "M" + start.x + "," + start.y + " L" + end.x + "," + end.y;
    return "M" + start.x + "," + start.y + " Q" + cx + "," + cy + " " + end.x + "," + end.y;
  }

  // Optional, at member level: an edge between two modules leaves its cluster at the edge facing the other
  // module and enters the other cluster the same way. All edges between the same two modules share those
  // two points, so they run together as one bundle between the clusters and only fan out inside them. Each
  // direction and edge kind gets its own lane, a few pixels to the side.
  var bundleLine = d3.line().curve(d3.curveBundle.beta(0.95));
  function bundledPath(e, s, t) {
    var a = graph.clusters.get(s.module);
    var b = graph.clusters.get(t.module);
    var ux = b.x - a.x;
    var uy = b.y - a.y;
    var length = Math.hypot(ux, uy) || 1;
    ux /= length;
    uy /= length;
    var lane = 3 + 5 * KIND_ORDER[e.kind];
    var exit = boxExit(a, ux, uy);
    var entry = boxExit(b, -ux, -uy);
    var from = { x: exit.x - uy * lane, y: exit.y + ux * lane };
    var to = { x: entry.x - uy * lane, y: entry.y + ux * lane };
    var start = pointTowards(s, from.x, from.y, s.r + 1);
    var end = pointTowards(t, to.x, to.y, t.r + 2);
    // Each shared point is given twice so the spline passes close to it, which keeps the bundle tight.
    return bundleLine([[start.x, start.y], [from.x, from.y], [from.x, from.y], [to.x, to.y], [to.x, to.y], [end.x, end.y]]);
  }

  // Where a line from a cluster's centre in direction (ux, uy) leaves its box.
  function boxExit(box, ux, uy) {
    var distance = Math.min(Math.abs(ux) > 1e-9 ? box.w / Math.abs(ux) : Infinity, Math.abs(uy) > 1e-9 ? box.h / Math.abs(uy) : Infinity);
    return { x: box.x + ux * distance, y: box.y + uy * distance };
  }

  function pointTowards(node, x, y, distance) {
    var dx = x - node.x;
    var dy = y - node.y;
    var length = Math.sqrt(dx * dx + dy * dy) || 1;
    return { x: node.x + dx / length * distance, y: node.y + dy / length * distance };
  }

  // A smooth outline around a module's members and their names.
  function hullPath(points) {
    var padded = [];
    points.forEach(function (p) {
      padded.push([p.x + p.x0, p.y + p.y0], [p.x + p.x1, p.y + p.y0], [p.x + p.x1, p.y + p.y1], [p.x + p.x0, p.y + p.y1]);
    });
    var hull = d3.polygonHull(padded);
    return d3.line().curve(d3.curveCatmullRomClosed.alpha(0.5))(hull);
  }

  // The drawn elements, kept so each simulation tick only moves them.
  var drawn = { hulls: null, edges: null, nodes: null };

  function render() {
    hideTooltip();
    var full = buildFullGraph();
    graph = full;
    if (state.focus) {
      var start = selectedIds(full);
      if (start.length === 0) {
        state.focus = false;
      } else {
        var reached = reach(full, start, state.reach === "transitive");
        graph = indexGraph(
          full.nodes.filter(function (n) { return reached.nodes.has(n.id); }),
          full.edges.filter(function (e) { return reached.edges.has(e); }));
      }
    }

    applyStyleSettings();
    initialLayout(graph);
    svg.classed("members", state.level === "members");

    // Module outlines (member level only).
    var groups = state.level === "members" ? d3.groups(graph.nodes, function (n) { return n.module; }) : [];
    drawn.hulls = hullLayer.selectAll("g.hull-group").data(groups, function (g) { return g[0]; })
      .join(function (enter) {
        var g = enter.append("g").attr("class", "hull-group");
        g.append("path").attr("class", "hull");
        g.append("text").attr("class", "hull-label").attr("text-anchor", "middle");
        return g;
      });
    drawn.hulls.attr("data-module", function (g) { return g[0]; });
    drawn.hulls.select("path.hull")
      .on("click", function (event, g) { event.stopPropagation(); vb.select(g[0]); })
      .call(dragBehaviour(function (g) { return g[1]; }));
    drawn.hulls.select("text.hull-label").text(function (g) { return g[0]; });

    drawn.edges = edgeLayer.selectAll("path.edge").data(graph.edges, function (e) { return e.from + "|" + e.to + "|" + e.kind; })
      .join("path")
      .attr("class", function (e) { return "edge " + e.kind + (e.from === e.to ? " self-loop" : ""); })
      .attr("marker-end", function (e) { return "url(#arrow-" + e.kind + ")"; })
      .attr("data-from", function (e) { return e.from; })
      .attr("data-to", function (e) { return e.to; })
      .style("stroke-width", function (e) { return (1.25 + Math.min(1.5, Math.log2(e.count) * 0.4)) + "px"; });
    drawn.edges.selectAll("title").data(function (e) { return [e]; }).join("title")
      .text(function (e) {
        var verb = e.kind === "implements" ? " implements " : e.kind === "events" ? " handles or raises events of " : " uses ";
        return e.from + verb + e.to + (e.count > 1 ? " (" + e.count + " references)" : "");
      });

    drawn.nodes = nodeLayer.selectAll("g.node").data(graph.nodes, function (n) { return n.id; })
      .join(function (enter) {
        var g = enter.append("g");
        g.append("circle").attr("class", "hit");
        g.append("path").attr("class", "shape");
        g.append("text").attr("class", "label");
        return g;
      });
    drawn.nodes
      .attr("class", function (n) {
        return "node group-" + n.group + (n.hollow ? " hollow" : "") + (n.isModuleNode ? " module-node" : "") +
          (n.unreferenced ? " unreferenced" : "");
      })
      .attr("data-id", function (n) { return n.id; })
      .on("click", function (event, n) { event.stopPropagation(); vb.select(n.id); })
      .on("dblclick", function (event, n) { event.stopPropagation(); release([n]); })
      .on("mouseenter", function (event, n) { if (!dragging) showTooltip(event, n); })
      .on("mousemove", function (event) { moveTooltip(event); })
      .on("mouseleave", hideTooltip)
      .call(dragBehaviour(function (n) { return [n]; }));
    drawn.nodes.select("circle.hit").attr("r", function (n) { return n.r + 6; });
    drawn.nodes.select("path.shape").attr("d", function (n) { return d3.symbol(n.shape, Math.PI * n.r * n.r)(); });
    drawn.nodes.select("text.label")
      .classed("minor", function (n) { return !!n.minor; })
      .attr("x", function (n) { return n.r + 5; })
      .attr("dy", "0.32em")
      .text(function (n) { return n.label; });

    updatePositions();
    updatePinned();
    message.hidden = graph.nodes.length > 0;
    message.textContent = graph.nodes.length > 0 ? "" : "Nothing to show with the current filters.";
    renderLegend();
    updateToolbar();
    fit(false);
    highlight();
  }

  // Moves everything to the nodes' current positions: after the starting layout and on every tick.
  function updatePositions() {
    if (!graph || !drawn.nodes) return;
    if (state.level === "members") {
      graph.clusters = new Map();
      d3.group(graph.nodes, function (n) { return n.module; }).forEach(function (members, id) {
        var b = moduleBox(members);
        graph.clusters.set(id, { x: (b.x0 + b.x1) / 2, y: (b.y0 + b.y1) / 2, w: (b.x1 - b.x0) / 2, h: (b.y1 - b.y0) / 2, top: b.y0 });
      });
      drawn.hulls.select("path.hull").attr("d", function (g) { return hullPath(g[1]); });
      drawn.hulls.select("text.hull-label")
        .attr("x", function (g) { return graph.clusters.get(g[0]).x; })
        .attr("y", function (g) { return graph.clusters.get(g[0]).top - 8; });
    } else {
      graph.clusters = null;
    }
    drawn.edges.attr("d", edgePath);
    drawn.nodes.attr("transform", function (n) { return "translate(" + n.x + "," + n.y + ")"; });
  }

  // ---- Dragging ----
  //
  // Dragging a node moves it; dragging a module's outline moves all its members. With "Rearrange while
  // dragging" on, the simulation runs so everything else makes room and follows its edges. A small move is
  // still a click, so clicking to select works as before.

  var dragging = false;

  function dragBehaviour(nodesOf) {
    return d3.drag()
      .clickDistance(4)
      .on("start", function (event, d) {
        dragging = true;
        hideTooltip();
        svg.classed("dragging", true);
        nodesOf(d).forEach(function (n) { n.fx = n.x; n.fy = n.y; });
        if (settings.live) simulation.alphaTarget(0.3).restart();
      })
      .on("drag", function (event, d) {
        nodesOf(d).forEach(function (n) {
          n.fx += event.dx;
          n.fy += event.dy;
          if (!settings.live) { n.x = n.fx; n.y = n.fy; }
        });
        if (!settings.live) updatePositions();
      })
      .on("end", function (event, d) {
        dragging = false;
        svg.classed("dragging", false);
        if (settings.live) simulation.alphaTarget(0);
        var nodes = nodesOf(d);
        if (settings.pinDragged) {
          nodes.forEach(function (n) { n.pinned = true; });
          updatePinned();
        } else {
          release(nodes);
        }
      });
  }

  function release(nodes) {
    nodes.forEach(function (n) { n.fx = null; n.fy = null; n.pinned = false; });
    updatePinned();
  }

  function updatePinned() {
    if (drawn.nodes) drawn.nodes.classed("pinned", function (n) { return !!n.pinned; });
  }

  function highlight() {
    if (!graph) return;
    var start = selectedIds(graph);
    var reached = start.length ? reach(graph, start, state.reach === "transitive") : null;

    nodeLayer.selectAll("g.node")
      .classed("selected", function (n) { return !!reached && reached.selected.has(n.id); })
      .classed("related", function (n) { return !!reached && reached.nodes.has(n.id) && !reached.selected.has(n.id); })
      .classed("dim", function (n) { return !!reached && !reached.nodes.has(n.id); });
    edgeLayer.selectAll("path.edge")
      .classed("highlighted", function (e) { return !!reached && reached.edges.has(e); })
      .classed("dim", function (e) { return !!reached && !reached.edges.has(e); });
    hullLayer.selectAll("g.hull-group")
      .classed("dim", function (g) { return !!reached && !g[1].some(function (n) { return reached.nodes.has(n.id); }); });

    updateToolbar();
    // A hidden view has no size, and moving it then would give d3-zoom a NaN transform.
    if (start.length === 1 && vb.activeView() === "graph") bringIntoView(graph.byId.get(start[0]));
  }

  // ---- Zoom helpers ----

  function fit(animate) {
    if (!graph || graph.nodes.length === 0) return;
    var width = view.clientWidth || 800;
    var height = view.clientHeight || 600;
    // Member-level nodes know their label's extent; module-level ones get room for a label on the right.
    var x0 = d3.min(graph.nodes, function (n) { return n.x - n.r; }) - 40;
    var x1 = d3.max(graph.nodes, function (n) { return state.level === "members" ? n.x + n.x1 : n.x + n.r + 140; }) + 20;
    var y0 = d3.min(graph.nodes, function (n) { return n.y - n.r; }) - 60;   // and module names above
    var y1 = d3.max(graph.nodes, function (n) { return n.y + n.r; }) + 40;
    var top = 48; // keep clear of the toolbar
    var legend = document.getElementById("graph-legend");
    var left = legend.open && width > 700 ? legend.offsetWidth + 16 : 0; // and of the open legend, if there's room
    var scale = Math.min(2, 0.95 * Math.min((width - left) / (x1 - x0), (height - top) / (y1 - y0)));
    var transform = d3.zoomIdentity
      .translate(left + (width - left) / 2, top + (height - top) / 2)
      .scale(scale)
      .translate(-(x0 + x1) / 2, -(y0 + y1) / 2);
    (animate ? svg.transition().duration(300) : svg).call(zoom.transform, transform);
  }

  function bringIntoView(node) {
    if (!node) return;
    var t = d3.zoomTransform(svg.node());
    var x = t.applyX(node.x);
    var y = t.applyY(node.y);
    var width = view.clientWidth;
    var height = view.clientHeight;
    if (x > 40 && x < width - 40 && y > 60 && y < height - 40) return;
    svg.transition().duration(300).call(zoom.translateTo, node.x, node.y);
  }

  // ---- Tooltip ----

  function showTooltip(event, n) {
    var uses = graph.outgoing.get(n.id).length;
    var usedBy = graph.incoming.get(n.id).length;
    tooltip.innerHTML = "";
    var name = document.createElement("strong");
    name.textContent = state.level === "members" && !n.isModuleNode ? n.module + "." + n.label : n.label;
    var kind = document.createElement("span");
    kind.textContent = n.title;
    var counts = document.createElement("span");
    counts.textContent = "Uses " + uses + " · used by " + usedBy + (n.unreferenced ? " · unreferenced" : "") +
      (n.pinned ? " · pinned (double-click to release)" : "");
    tooltip.append(name, kind, counts);
    tooltip.hidden = false;
    moveTooltip(event);
  }

  function moveTooltip(event) {
    if (tooltip.hidden) return;
    var box = view.getBoundingClientRect();
    var x = event.clientX - box.left + 14;
    var y = event.clientY - box.top + 14;
    if (x + tooltip.offsetWidth > box.width - 8) x = event.clientX - box.left - tooltip.offsetWidth - 14;
    if (y + tooltip.offsetHeight > box.height - 8) y = event.clientY - box.top - tooltip.offsetHeight - 14;
    tooltip.style.left = Math.max(4, x) + "px";
    tooltip.style.top = Math.max(4, y) + "px";
  }

  function hideTooltip() { tooltip.hidden = true; }

  // ---- Legend ----

  function legendIcon(shape, className, size) {
    var r = size || 6;
    return "<svg width=\"16\" height=\"16\" viewBox=\"-8 -8 16 16\" aria-hidden=\"true\" class=\"graph-art\">" +
      "<g class=\"node " + className + "\"><path class=\"shape\" d=\"" + d3.symbol(shape, Math.PI * r * r)() + "\"></path></g></svg>";
  }

  function edgeIcon(kind) {
    return "<svg width=\"34\" height=\"10\" viewBox=\"0 -5 34 10\" aria-hidden=\"true\" class=\"graph-art\">" +
      "<path class=\"edge " + kind + "\" d=\"M1,0 L30,0\" marker-end=\"url(#arrow-" + kind + ")\"></path></svg>";
  }

  function item(icon, text) { return "<li>" + icon + "<span>" + text + "</span></li>"; }

  function renderLegend() {
    var html = "";
    if (state.level === "modules") {
      html += "<ul>" +
        item(legendIcon(d3.symbolSquare, "group-code"), "Standard module") +
        item(legendIcon(d3.symbolCircle, "group-class"), "Class") +
        item(legendIcon(d3.symbolCircle, "group-class hollow"), "Interface") +
        item(legendIcon(d3.symbolDiamond, "group-host"), "UserForm") +
        item(legendIcon(d3.symbolTriangle, "group-host"), "Document module") +
        "</ul><p class=\"muted\" style=\"margin:4px 0 0\">Size: number of members</p>";
    } else {
      html += "<h3>Member</h3><ul>" + MEMBER_CATEGORIES.filter(function (c) { return c.id !== "enumValue"; }).map(function (c) {
        return item(legendIcon(c.shape, "group-neutral"), c.label);
      }).join("") +
        item(legendIcon(d3.symbolCircle, "group-code module-node", 5), "The module itself") +
        item(legendIcon(d3.symbolCircle, "group-code unreferenced", 5), "Unreferenced") +
        "</ul><h3>Module (colour and outline)</h3><ul>" +
        item(legendIcon(d3.symbolSquare, "group-code"), "Standard module") +
        item(legendIcon(d3.symbolSquare, "group-class"), "Class or interface") +
        item(legendIcon(d3.symbolSquare, "group-host"), "UserForm or document") +
        "</ul>";
    }
    html += "<h3>Edges</h3><ul>" + EDGE_KINDS.map(function (k) { return item(edgeIcon(k.id), k.label); }).join("") + "</ul>";
    document.getElementById("graph-legend-body").innerHTML = html;
  }

  // ---- Toolbar ----

  function setChecked(selector, attribute, value) {
    view.querySelectorAll(selector).forEach(function (b) {
      b.setAttribute("aria-checked", b.getAttribute(attribute) === value ? "true" : "false");
    });
  }

  function updateToolbar() {
    setChecked("[data-level]", "data-level", state.level);
    setChecked("[data-reach]", "data-reach", state.reach);
    var hasSelection = !!graph && selectedIds(graph).length > 0;
    focusButton.disabled = !hasSelection && !state.focus;
    focusButton.textContent = state.focus ? "Show all" : "Focus";
    focusButton.setAttribute("aria-pressed", state.focus ? "true" : "false");
    filtersBox.hidden = state.level !== "members";
  }

  view.querySelectorAll("[data-level]").forEach(function (button) {
    button.addEventListener("click", function () {
      if (state.level === button.getAttribute("data-level")) return;
      state.level = button.getAttribute("data-level");
      state.focus = false;
      saveSetting("vbadoc-graph-level", state.level);
      render();
    });
  });

  view.querySelectorAll("[data-reach]").forEach(function (button) {
    button.addEventListener("click", function () {
      state.reach = button.getAttribute("data-reach");
      saveSetting("vbadoc-graph-reach", state.reach);
      if (state.focus) render(); else highlight();
    });
  });

  focusButton.addEventListener("click", function () {
    state.focus = !state.focus;
    render();
  });

  document.getElementById("graph-fit").addEventListener("click", function () { fit(true); });

  var filterMenu = document.getElementById("graph-filter-menu");
  filterMenu.innerHTML = MEMBER_CATEGORIES.map(function (c) {
    return "<label><input type=\"checkbox\" data-category=\"" + c.id + "\" checked> " + c.label + "</label>";
  }).join("") + "<hr><label><input type=\"checkbox\" id=\"graph-unreferenced-only\"> Only unreferenced members</label>";
  filterMenu.addEventListener("change", function (event) {
    var box = event.target;
    if (box.id === "graph-unreferenced-only") state.unreferencedOnly = box.checked;
    else if (box.checked) state.hiddenCategories.delete(box.getAttribute("data-category"));
    else state.hiddenCategories.add(box.getAttribute("data-category"));
    render();
  });

  // ---- Settings panel ----
  //
  // Built from CONTROLS. Settings are remembered per browser. Changes apply straight away: looks restyle,
  // layout settings wake the simulation so the graph moves to its new shape, and node or text size (which
  // change what the nodes are) redraw the graph.

  var settingsMenu = document.getElementById("graph-settings-menu");

  function formatValue(control, value) {
    return (control.unit === "×" ? Number(value).toFixed(1) : String(value)) + (control.unit || "");
  }

  function buildSettingsMenu() {
    var html = "";
    CONTROLS.forEach(function (c) {
      if (c.section) { html += "<h3>" + c.section + "</h3>"; return; }
      var note = c.note ? "<small>" + c.note + "</small>" : "";
      var id = "graph-setting-" + c.key;
      if (c.type === "checkbox") {
        html += "<label class=\"check\"><input type=\"checkbox\" id=\"" + id + "\" data-setting=\"" + c.key + "\"" +
          (settings[c.key] ? " checked" : "") + "><span>" + c.label + note + "</span></label>";
      } else if (c.type === "select") {
        html += "<label class=\"field\" for=\"" + id + "\"><span>" + c.label + note + "</span></label><select id=\"" + id +
          "\" data-setting=\"" + c.key + "\">" + c.options.map(function (o) {
            return "<option value=\"" + o[0] + "\"" + (settings[c.key] === o[0] ? " selected" : "") + ">" + o[1] + "</option>";
          }).join("") + "</select>";
      } else {
        html += "<label class=\"field\" for=\"" + id + "\"><span>" + c.label + note + "</span><output>" +
          formatValue(c, settings[c.key]) + "</output></label><input type=\"range\" id=\"" + id + "\" data-setting=\"" + c.key +
          "\" min=\"" + c.min + "\" max=\"" + c.max + "\" step=\"" + c.step + "\" value=\"" + settings[c.key] + "\">";
      }
    });
    html += "<div class=\"actions\">" +
      "<button type=\"button\" class=\"button\" id=\"graph-release-all\" title=\"Let every pinned node move again\">Release pinned</button>" +
      "<button type=\"button\" class=\"button\" id=\"graph-relayout\" title=\"Start again from the starting layout\">Re-run layout</button>" +
      "<button type=\"button\" class=\"button\" id=\"graph-reset-settings\">Reset to defaults</button></div>";
    settingsMenu.innerHTML = html;
  }

  function applySetting(control, value) {
    settings[control.key] = value;
    saveSetting("vbadoc-graph-settings", JSON.stringify(settings));
    if (!graph) return;
    if (control.key === "nodeSize") {
      // Node sizes are set when nodes are made; resize the drawn ones in place, keeping their positions.
      var fresh = buildFullGraph();
      graph.nodes.forEach(function (n) { n.r = fresh.byId.get(n.id).r; });
      drawn.nodes.select("circle.hit").attr("r", function (n) { return n.r + 6; });
      drawn.nodes.select("path.shape").attr("d", function (n) { return d3.symbol(n.shape, Math.PI * n.r * n.r)(); });
      drawn.nodes.select("text.label").attr("x", function (n) { return n.r + 5; });
    }
    applyStyleSettings();
    if (control.layout) relayout();
    else updatePositions();
  }

  settingsMenu.addEventListener("input", function (event) {
    var input = event.target;
    var key = input.getAttribute("data-setting");
    if (!key) return;
    var control = CONTROLS.filter(function (c) { return c.key === key; })[0];
    var value = control.type === "checkbox" ? input.checked : control.type === "range" ? Number(input.value) : input.value;
    if (control.type === "range") input.previousElementSibling.querySelector("output").textContent = formatValue(control, value);
    applySetting(control, value);
  });

  settingsMenu.addEventListener("click", function (event) {
    var id = event.target.id;
    if (id === "graph-release-all" && graph) {
      release(graph.nodes);
      if (settings.live) simulation.alpha(0.3).restart();
    } else if (id === "graph-relayout") {
      render();
    } else if (id === "graph-reset-settings") {
      settings = Object.assign({}, DEFAULTS);
      saveSetting("vbadoc-graph-settings", JSON.stringify(settings));
      buildSettingsMenu();
      if (graph) render();
    }
  });

  buildSettingsMenu();

  // ---- Wiring to the rest of the page ----

  var started = false;
  function drawIfVisible() {
    if (vb.activeView() !== "graph") return;
    if (started) { highlight(); return; } // catch up with a selection made in the other view
    started = true;
    render();
  }

  vb.onSelectionChange(function () {
    if (!started) return;
    if (state.focus) render(); else highlight();
  });
  vb.onViewChange(drawIfVisible);
  // Colours are CSS variables, so a theme change needs no redraw.

  // When the view changes size (the source panel is resized or hidden, or the window), keep what was in the
  // middle in the middle. A hidden view has no size, so changes while it's hidden are skipped.
  var lastSize = null;
  if (window.ResizeObserver) {
    new ResizeObserver(function () {
      var width = view.clientWidth;
      var height = view.clientHeight;
      if (width === 0 || height === 0) { lastSize = null; return; }
      if (started && lastSize) {
        var k = d3.zoomTransform(svg.node()).k;
        svg.call(zoom.translateBy, (width - lastSize.width) / 2 / k, (height - lastSize.height) / 2 / k);
      }
      lastSize = { width: width, height: height };
    }).observe(view);
  }

  window.vbadocGraph = {
    level: function () { return state.level; },
    nodeCount: function () { return graph ? graph.nodes.length : 0; },
    edgeCount: function () { return graph ? graph.edges.length : 0; },
    // For tests: whether the simulation is still moving things.
    moving: function () { return !!simulation && simulation.alpha() > simulation.alphaMin(); },
    position: function (id) { var n = graph && graph.byId.get(id); return n ? { x: n.x, y: n.y } : null; }
  };

  drawIfVisible();
})();
