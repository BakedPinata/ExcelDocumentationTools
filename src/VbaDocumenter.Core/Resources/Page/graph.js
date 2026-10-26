// graph.js: the dependency graph view, drawn with d3.
//
// Two levels: one node per module, or one node per member with each module's members drawn as a cluster
// inside a shaded outline. Edges are Uses (solid), Implements (dashed, hollow arrow) and Events (dotted).
// Clicking a node selects it through window.vbadoc, which shows its source; the graph then highlights what
// the selection depends on and what depends on it, directly or transitively.
//
// The layout is computed in one go (not animated). d3-force starts from fixed positions, so the same
// project always gets the same picture.
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
    if (state.level === "modules") {
      data.modules.forEach(function (m) {
        var style = moduleStyle(m);
        nodes.push({
          id: m.id, module: m.id, label: m.id, group: style.group, shape: style.shape, hollow: !!style.hollow,
          r: Math.min(20, 7 + 1.6 * Math.sqrt(memberCountByModule.get(m.id) || 0)), title: style.label, isModule: true
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
          r: m.kind === "enumMember" ? 4 : 5.5, title: (KIND_LABELS[m.kind] || m.kind) + " in " + m.module,
          unreferenced: m.unreferenced, minor: true
        });
      });
      // Each module also has a node for references to the module itself (e.g. "As Order", "Sheet1.Range").
      data.modules.forEach(function (m) {
        nodes.push({
          id: m.id, module: m.id, label: m.id, group: moduleStyle(m).group, shape: d3.symbolCircle, r: 7,
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

  function layOut(graph) {
    var nodes = graph.nodes;
    var links = graph.edges.filter(function (e) { return e.from !== e.to; }).map(function (e) { return { source: e.from, target: e.to }; });

    if (state.level === "modules") {
      // Labels sit to the right of each node, so leave room for them when keeping nodes apart.
      var simulation = d3.forceSimulation(nodes)
        .force("link", d3.forceLink(links).id(function (d) { return d.id; }).distance(150).strength(0.3))
        .force("charge", d3.forceManyBody().strength(-900))
        .force("collide", d3.forceCollide(function (d) { return Math.max(d.r + 20, (d.r + labelWidth(d)) / 2 + 12); }))
        .force("x", d3.forceX(0).strength(0.05))
        .force("y", d3.forceY(0).strength(0.05))
        .stop();
      for (var i = 0; i < 300; i++) simulation.tick();
      return;
    }

    // Member level: first place one circle per module, sized for its members, so related modules sit
    // together without overlapping; then pull each member towards its module's place.
    var centers = clusterCenters(graph);
    var placedInModule = new Map();
    nodes.forEach(function (n) {
      var c = centers.get(n.module);
      var k = placedInModule.get(n.module) || 0;
      placedInModule.set(n.module, k + 1);
      var angle = k * 2.39996; // golden angle: an even, fixed spiral
      var radius = 6 * Math.sqrt(k);
      n.x = c.x + radius * Math.cos(angle);
      n.y = c.y + radius * Math.sin(angle);
    });
    var memberSimulation = d3.forceSimulation(nodes)
      .force("link", d3.forceLink(links).id(function (d) { return d.id; }).distance(40).strength(0.02))
      .force("charge", d3.forceManyBody().strength(-45).distanceMax(140))
      .force("collide", d3.forceCollide(function (d) { return d.r + 9; }))
      .force("x", d3.forceX(function (d) { return centers.get(d.module).x; }).strength(0.25))
      .force("y", d3.forceY(function (d) { return centers.get(d.module).y; }).strength(0.25))
      .stop();
    for (var j = 0; j < 250; j++) memberSimulation.tick();
  }

  // Rough width of a node's label in pixels (12px system font), for spacing.
  function labelWidth(n) { return n.label.length * 7; }

  function clusterCenters(graph) {
    var counts = new Map();
    graph.nodes.forEach(function (n) { counts.set(n.module, (counts.get(n.module) || 0) + 1); });
    var clusters = Array.from(counts.keys()).map(function (id) { return { id: id, r: 30 + 16 * Math.sqrt(counts.get(id)) }; });
    var seen = new Set();
    var links = [];
    graph.edges.forEach(function (e) {
      var a = graph.byId.get(e.from).module;
      var b = graph.byId.get(e.to).module;
      var key = a < b ? a + "\u0000" + b : b + "\u0000" + a;
      if (a !== b && !seen.has(key)) { seen.add(key); links.push({ source: a, target: b }); }
    });
    var simulation = d3.forceSimulation(clusters)
      .force("link", d3.forceLink(links).id(function (d) { return d.id; })
        .distance(function (l) { return l.source.r + l.target.r + 40; }).strength(0.2))
      .force("charge", d3.forceManyBody().strength(-150))
      .force("collide", d3.forceCollide(function (d) { return d.r + 34; }))
      .force("x", d3.forceX(0).strength(0.04))
      .force("y", d3.forceY(0).strength(0.04))
      .stop();
    for (var i = 0; i < 300; i++) simulation.tick();
    var centers = new Map();
    clusters.forEach(function (c) { centers.set(c.id, { x: c.x, y: c.y }); });
    return centers;
  }

  // ---- Drawing ----

  var graph = null;
  var zoom = d3.zoom().scaleExtent([0.05, 6]).on("zoom", function (event) {
    viewport.attr("transform", event.transform);
    // Member names appear once zoomed in; until then only the selection and its neighbours are labelled.
    svg.classed("all-labels", event.transform.k >= 1.3);
    hideTooltip();
  });
  svg.call(zoom).on("dblclick.zoom", null);
  svg.on("click", function (event) { if (event.target === svg.node()) vb.select(null); });

  var KIND_ORDER = { uses: 0, implements: 1, events: 2 };

  // A gently curved edge. Edges of different kinds between the same two nodes, and edges in opposite
  // directions, curve apart instead of overlapping. A node's edge to itself (recursion) is a loop.
  function edgePath(e) {
    var s = graph.byId.get(e.from);
    var t = graph.byId.get(e.to);
    if (s === t) {
      var r = s.r;
      return "M" + (s.x + r * 0.7) + "," + (s.y - r * 0.7) +
        " C" + (s.x + r * 4) + "," + (s.y - r * 4) + " " + (s.x + r * 4.5) + "," + (s.y + r * 1.5) +
        " " + (s.x + r + 1) + "," + (s.y + r * 0.4);
    }
    var dx = t.x - s.x;
    var dy = t.y - s.y;
    var length = Math.sqrt(dx * dx + dy * dy) || 1;
    var bend = 0.12 + 0.14 * KIND_ORDER[e.kind];
    var cx = (s.x + t.x) / 2 - dy * bend;
    var cy = (s.y + t.y) / 2 + dx * bend;
    var start = pointTowards(s, cx, cy, s.r + 1);
    var end = pointTowards(t, cx, cy, t.r + 2);
    return "M" + start.x + "," + start.y + " Q" + cx + "," + cy + " " + end.x + "," + end.y;
  }

  function pointTowards(node, x, y, distance) {
    var dx = x - node.x;
    var dy = y - node.y;
    var length = Math.sqrt(dx * dx + dy * dy) || 1;
    return { x: node.x + dx / length * distance, y: node.y + dy / length * distance };
  }

  // A smooth outline around a module's members, a little larger than the nodes.
  function hullPath(points) {
    var padded = [];
    points.forEach(function (p) {
      for (var a = 0; a < 8; a++) {
        padded.push([p.x + (p.r + 14) * Math.cos(a * Math.PI / 4), p.y + (p.r + 14) * Math.sin(a * Math.PI / 4)]);
      }
    });
    var hull = d3.polygonHull(padded);
    return d3.line().curve(d3.curveCatmullRomClosed.alpha(0.5))(hull);
  }

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

    layOut(graph);
    svg.classed("members", state.level === "members");

    // Module outlines (member level only).
    var groups = state.level === "members" ? d3.groups(graph.nodes, function (n) { return n.module; }) : [];
    var hulls = hullLayer.selectAll("g.hull-group").data(groups, function (g) { return g[0]; })
      .join(function (enter) {
        var g = enter.append("g").attr("class", "hull-group");
        g.append("path").attr("class", "hull");
        g.append("text").attr("class", "hull-label").attr("text-anchor", "middle");
        return g;
      });
    hulls.attr("data-module", function (g) { return g[0]; });
    hulls.select("path.hull").attr("d", function (g) { return hullPath(g[1]); })
      .on("click", function (event, g) { event.stopPropagation(); vb.select(g[0]); });
    hulls.select("text.hull-label")
      .attr("x", function (g) { return d3.mean(g[1], function (n) { return n.x; }); })
      .attr("y", function (g) { return d3.min(g[1], function (n) { return n.y - n.r; }) - 20; })
      .text(function (g) { return g[0]; });

    edgeLayer.selectAll("path.edge").data(graph.edges, function (e) { return e.from + "|" + e.to + "|" + e.kind; })
      .join("path")
      .attr("class", function (e) { return "edge " + e.kind + (e.from === e.to ? " self-loop" : ""); })
      .attr("d", edgePath)
      .attr("marker-end", function (e) { return "url(#arrow-" + e.kind + ")"; })
      .attr("data-from", function (e) { return e.from; })
      .attr("data-to", function (e) { return e.to; })
      .style("stroke-width", function (e) { return (1.25 + Math.min(1.5, Math.log2(e.count) * 0.4)) + "px"; })
      .selectAll("title").data(function (e) { return [e]; }).join("title")
      .text(function (e) {
        var verb = e.kind === "implements" ? " implements " : e.kind === "events" ? " handles or raises events of " : " uses ";
        return e.from + verb + e.to + (e.count > 1 ? " (" + e.count + " references)" : "");
      });

    var nodes = nodeLayer.selectAll("g.node").data(graph.nodes, function (n) { return n.id; })
      .join(function (enter) {
        var g = enter.append("g");
        g.append("circle").attr("class", "hit");
        g.append("path").attr("class", "shape");
        g.append("text").attr("class", "label");
        return g;
      });
    nodes
      .attr("class", function (n) {
        return "node group-" + n.group + (n.hollow ? " hollow" : "") + (n.isModuleNode ? " module-node" : "") +
          (n.unreferenced ? " unreferenced" : "");
      })
      .attr("data-id", function (n) { return n.id; })
      .attr("transform", function (n) { return "translate(" + n.x + "," + n.y + ")"; })
      .on("click", function (event, n) { event.stopPropagation(); vb.select(n.id); })
      .on("mouseenter", function (event, n) { showTooltip(event, n); })
      .on("mousemove", function (event) { moveTooltip(event); })
      .on("mouseleave", hideTooltip);
    nodes.select("circle.hit").attr("r", function (n) { return n.r + 6; });
    nodes.select("path.shape").attr("d", function (n) { return d3.symbol(n.shape, Math.PI * n.r * n.r)(); });
    nodes.select("text.label")
      .classed("minor", function (n) { return !!n.minor; })
      .attr("x", function (n) { return n.r + 5; })
      .attr("dy", "0.32em")
      .text(function (n) { return n.label; });

    message.hidden = graph.nodes.length > 0;
    message.textContent = graph.nodes.length > 0 ? "" : "Nothing to show with the current filters.";
    renderLegend();
    updateToolbar();
    fit(false);
    highlight();
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
    if (start.length === 1) bringIntoView(graph.byId.get(start[0]));
  }

  // ---- Zoom helpers ----

  function fit(animate) {
    if (!graph || graph.nodes.length === 0) return;
    var width = view.clientWidth || 800;
    var height = view.clientHeight || 600;
    var x0 = d3.min(graph.nodes, function (n) { return n.x - n.r; }) - 40;
    var x1 = d3.max(graph.nodes, function (n) { return n.x + n.r; }) + 140; // room for labels on the right
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
    counts.textContent = "Uses " + uses + " · used by " + usedBy + (n.unreferenced ? " · unreferenced" : "");
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

  // ---- Wiring to the rest of the page ----

  var drawn = false;
  function drawIfVisible() {
    if (vb.activeView() !== "graph" || drawn) return;
    drawn = true;
    render();
  }

  vb.onSelectionChange(function () {
    if (!drawn) return;
    if (state.focus) render(); else highlight();
  });
  vb.onViewChange(drawIfVisible);
  // Colours are CSS variables, so a theme change needs no redraw.

  window.vbadocGraph = {
    level: function () { return state.level; },
    nodeCount: function () { return graph ? graph.nodes.length : 0; },
    edgeCount: function () { return graph ? graph.edges.length : 0; }
  };

  drawIfVisible();
})();
