// classes.js: the class diagram view, drawn with d3.
//
// One box per module: a stereotype for its kind and its name, then its attributes and its methods, as the C# side
// (ClassDiagram) lists them. Relationships come from the page data: realisation (implements), association (held
// in a module-level variable, labelled with the variable) and dependency (any other use). All text is set with
// textContent, so no name or value can be read as markup.
//
// The layout is worked out before anything is drawn, so the same project and Show… choices always give the same
// first picture. Each module sits in a layer above the modules it uses, so arrows point down and modules used
// almost everywhere sink to the bottom; modules that use each other in a circle share a layer. Within a layer,
// modules are ordered to keep related boxes near each other, and a very wide layer wraps onto more rows. Modules
// with no relationships are set out in a grid underneath.
//
// Boxes can then be dragged. Where they're dropped is remembered per workbook and Show… choice, until
// "Re-run layout". When the selection is focused (the page's focus on click), the smaller diagram is laid out afresh
// and moves aren't remembered.
(function () {
  "use strict";

  var vb = window.vbadoc;
  var data = vb.data;

  var STEREOTYPES = { standard: "module", "class": "class", form: "form", document: "document" };
  function stereotype(module) { return module.isInterface ? "interface" : STEREOTYPES[module.kind] || "class"; }

  // ---- Settings (remembered per browser; storage can fail on file://) ----

  function loadSetting(key, fallback) { try { var v = localStorage.getItem(key); return v === null ? fallback : v === "true"; } catch (e) { return fallback; } }
  function saveSetting(key, value) { try { localStorage.setItem(key, String(value)); } catch (e) { /* not remembered */ } }
  function loadJson(key) { try { var v = localStorage.getItem(key); return v ? JSON.parse(v) : null; } catch (e) { return null; } }
  function saveJson(key, value) {
    try { if (value) localStorage.setItem(key, JSON.stringify(value)); else localStorage.removeItem(key); } catch (e) { /* not remembered */ }
  }

  var OPTIONS = [
    { key: "members", label: "Members" },
    { key: "private", label: "Private members" },
    { key: "dependencies", label: "Dependencies (other uses)" }
  ];

  var state = {
    focusModule: null,  // the module the focused diagram is drawn around, or null when it shows every module
    resetView: true,    // the next drawing starts from the starting view rather than keeping the current one
    show: {},
    legend: new Set()   // legend items clicked to light up what they stand for (not remembered)
  };
  OPTIONS.forEach(function (o) { state.show[o.key] = loadSetting("vbadoc-classes-" + o.key, true); });

  // Moved boxes, per workbook (every page opened from disk shares one localStorage) and per Show… choice, since
  // those change the boxes' sizes and so the layout.
  function positionsKey() {
    return "vbadoc-classes-positions|" + data.project.title + "|" +
      OPTIONS.map(function (o) { return o.key.charAt(0) + (state.show[o.key] ? 1 : 0); }).join("");
  }

  // ---- Sizes (px). Text is measured in the font it's drawn in, so boxes fit their lines. ----

  var FONT_SIZE = 12;      // member lines
  var NAME_SIZE = 13;      // the module's name
  var STEREO_SIZE = 11;    // «class» and the like
  var LINE_HEIGHT = 17;
  var PAD_X = 10;          // between the box's sides and its text
  var PAD_Y = 5;           // above and below each compartment's lines
  var HEADER_HEIGHT = 2 * PAD_Y + 2 * LINE_HEIGHT;
  var EMPTY_COMPARTMENT = 8;
  var MIN_WIDTH = 110;
  var MAX_WIDTH = 520;     // longer lines are cut short with "…", and show in full in a tooltip
  var GAP_X = 48;          // between boxes in a row
  var ROW_GAP = 36;        // between the rows of one layer
  var LAYER_GAP = 84;      // between layers, where most edges run
  var CLICK_DISTANCE = 4;  // screen pixels a press may move and still be a click, as in the graph

  var measure = document.createElement("canvas").getContext("2d");
  var fontFamily = getComputedStyle(document.documentElement).getPropertyValue("--font-ui").trim() || "sans-serif";
  function textWidth(text, size, weight) {
    if (!measure) return text.length * size * 0.6;
    var font = (weight || "normal") + " " + size + "px " + fontFamily;
    if (measure.font !== font) measure.font = font;
    return measure.measureText(text).width;
  }

  // Cuts text with "…" so it fits the width.
  function fitText(text, width, size, weight) {
    if (textWidth(text, size, weight) <= width) return text;
    var low = 0, high = text.length;
    while (low < high) {
      var mid = Math.ceil((low + high) / 2);
      if (textWidth(text.slice(0, mid) + "…", size, weight) <= width) low = mid; else high = mid - 1;
    }
    return text.slice(0, low) + "…";
  }

  // ---- Elements ----

  var view = document.getElementById("view-classes");
  var svg = d3.select("#classes-svg");
  var viewport = svg.select("g.viewport");
  var edgeLayer = viewport.select("g.edges");
  var boxLayer = viewport.select("g.boxes");
  var labelLayer = viewport.select("g.edge-labels");
  var focusButton = document.getElementById("classes-focus");
  var filterMenu = document.getElementById("classes-filter-menu");

  var membersByModule = new Map();
  data.members.forEach(function (m) {
    if (!m.class) return;
    if (!membersByModule.has(m.module)) membersByModule.set(m.module, []);
    membersByModule.get(m.module).push(m);
  });
  var moduleIndex = new Map();
  data.modules.forEach(function (m, i) { moduleIndex.set(m.id, i); });

  // A member's line as shown: an attribute's text, or a method's name, parameters and return type.
  function lineText(line) {
    return line.parameters == null ? line.text : line.text + "(" + line.parameters + ")" + (line.type ? " : " + line.type : "");
  }

  // ---- What's shown ----

  function shownEdges() {
    return data.classEdges.filter(function (e) { return e.from !== e.to && (state.show.dependencies || e.kind !== "dependency"); });
  }

  // The modules the explorer's filter shows (all of them without one), or when focused the focused module and every
  // module a shown relationship links it with, of those.
  function shownModules() {
    var filtered = data.modules.filter(function (m) { return vb.inFilter(m.id); });
    if (!state.focusModule) return filtered.map(function (m) { return m.id; });
    var ids = new Set([state.focusModule]);
    shownEdges().forEach(function (e) {
      if (e.from === state.focusModule) ids.add(e.to);
      if (e.to === state.focusModule) ids.add(e.from);
    });
    return filtered.filter(function (m) { return ids.has(m.id); }).map(function (m) { return m.id; });
  }

  // Where boxes were moved is only kept for the whole diagram: a focused or filtered one is laid out afresh.
  function remembersMoves() { return !state.focusModule && !vb.filterActive(); }

  // A box's contents and size. Lines keep their member id, for clicks and highlighting.
  function buildBox(id) {
    var module = vb.modulesById.get(id);
    var lines = !state.show.members ? [] : (membersByModule.get(id) || []).filter(function (m) {
      return state.show["private"] || m.class.text.charAt(0) !== "-";
    }).map(function (m) { return { member: m.id, text: lineText(m.class), method: m.class.parameters != null }; });
    var box = {
      id: id,
      index: moduleIndex.get(id),
      stereotype: "«" + stereotype(module) + "»",
      kind: stereotype(module),
      attributes: lines.filter(function (l) { return !l.method; }),
      methods: lines.filter(function (l) { return l.method; })
    };
    var widest = Math.max(textWidth(id, NAME_SIZE, "600"), textWidth(box.stereotype, STEREO_SIZE));
    lines.forEach(function (l) { widest = Math.max(widest, textWidth(l.text, FONT_SIZE)); });
    box.width = Math.ceil(Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, widest + 2 * PAD_X)));
    function compartment(list) { return list.length ? 2 * PAD_Y + list.length * LINE_HEIGHT : EMPTY_COMPARTMENT; }
    box.height = HEADER_HEIGHT + (state.show.members ? compartment(box.attributes) + compartment(box.methods) : 0);
    return box;
  }

  // ---- Layout ----
  //
  // Sets each box's x and y (its top left corner).

  function layout(boxes, edges) {
    var uses = new Map(), usedBy = new Map();
    boxes.forEach(function (b) { uses.set(b.id, []); usedBy.set(b.id, []); });
    edges.forEach(function (e) { uses.get(e.from).push(e.to); usedBy.get(e.to).push(e.from); });
    var connected = boxes.filter(function (b) { return uses.get(b.id).length + usedBy.get(b.id).length > 0; });
    var loose = boxes.filter(function (b) { return uses.get(b.id).length + usedBy.get(b.id).length === 0; });
    var byId = new Map(boxes.map(function (b) { return [b.id, b]; }));

    // 1. Layers. Modules that use each other in a circle (a strongly connected group) share a layer, as queries
    //    in a circular dependency share a column in pqdoc. Between groups the uses can't go round in a circle, so
    //    each group goes one layer below the lowest group that uses it. Then a group that nothing uses moves down
    //    to just above the highest group it uses, so its edges stay short.
    var groups = stronglyConnected(connected.map(function (b) { return b.id; }), uses);
    var groupOf = new Map();
    groups.forEach(function (ids, g) { ids.forEach(function (id) { groupOf.set(id, g); }); });
    var groupUses = groups.map(function () { return new Set(); });
    var groupUsed = groups.map(function () { return false; });
    edges.forEach(function (e) {
      var from = groupOf.get(e.from), to = groupOf.get(e.to);
      if (from !== to) { groupUses[from].add(to); groupUsed[to] = true; }
    });
    var groupLayer = groups.map(function () { return 0; });
    groups.forEach(function (ids, g) { // users come before what they use
      groupUses[g].forEach(function (h) { groupLayer[h] = Math.max(groupLayer[h], groupLayer[g] + 1); });
    });
    groups.forEach(function (ids, g) {
      if (groupUsed[g] || groupUses[g].size === 0) return;
      groupLayer[g] = d3.min(Array.from(groupUses[g]), function (h) { return groupLayer[h]; }) - 1;
    });

    var layers = [], layerOf = new Map();
    connected.forEach(function (b) { // in module order, which the ordering below starts from
      var l = groupLayer[groupOf.get(b.id)];
      (layers[l] = layers[l] || []).push(b.id);
    });
    layers = layers.filter(Boolean);
    layers.forEach(function (ids, l) { ids.forEach(function (id) { layerOf.set(id, l); }); });

    // 2. Order within layers: a few sweeps down and up, each sorting a layer by where its neighbours above (or
    //    below) sit, as a share of their layer's width. Ties keep the current order, so the result is fixed.
    var share = new Map();
    function number(ids) { ids.forEach(function (id, i) { share.set(id, (i + 0.5) / ids.length); }); }
    layers.forEach(number);
    function neighbours(id) { return uses.get(id).concat(usedBy.get(id)); }
    for (var sweep = 0; sweep < 8; sweep++) {
      var down = sweep % 2 === 0;
      for (var step = 1; step < layers.length; step++) {
        var l = down ? step : layers.length - 1 - step;
        var key = new Map();
        layers[l].forEach(function (id, i) {
          var refs = neighbours(id).filter(function (n) { return down ? layerOf.get(n) < l : layerOf.get(n) > l; });
          key.set(id, { at: refs.length ? d3.mean(refs, function (n) { return share.get(n); }) : share.get(id), was: i });
        });
        layers[l].sort(function (a, b) { return key.get(a).at - key.get(b).at || key.get(a).was - key.get(b).was; });
        number(layers[l]);
      }
    }

    // 3. Rows: a layer wider than the limit wraps. The limit grows with the diagram, so big projects stay roughly
    //    as wide as they are tall.
    var area = d3.sum(boxes, function (b) { return (b.width + GAP_X) * (b.height + ROW_GAP); });
    var maxRow = Math.max(1400, Math.sqrt(area) * 1.6);
    function wrap(list) {
      var rows = [], row = [], width = 0;
      list.forEach(function (b) {
        if (row.length && width + GAP_X + b.width > maxRow) { rows.push(row); row = []; width = 0; }
        width += (row.length ? GAP_X : 0) + b.width;
        row.push(b);
      });
      if (row.length) rows.push(row);
      return rows;
    }
    var rows = [];
    layers.forEach(function (ids) { wrap(ids.map(function (id) { return byId.get(id); })).forEach(function (r, i) { rows.push({ boxes: r, first: i === 0 }); }); });

    // 4. Positions: rows stacked downwards, each packed and centred; then a few passes move each box towards the
    //    middle of its neighbours in other rows, keeping the row's order and spacing.
    var y = 0;
    rows.forEach(function (row, i) {
      if (i > 0) y += row.first ? LAYER_GAP : ROW_GAP;
      row.y = y;
      row.boxes.forEach(function (b) { b.y = y; });
      y += d3.max(row.boxes, function (b) { return b.height; });
      pack(row.boxes, row.boxes.map(function () { return 0; }));
    });
    var rowOf = new Map();
    rows.forEach(function (row, i) { row.boxes.forEach(function (b) { rowOf.set(b.id, i); }); });
    function centre(b) { return b.x + b.width / 2; }
    for (var pass = 0; pass < 6; pass++) {
      var order = pass % 2 === 0 ? rows : rows.slice().reverse();
      order.forEach(function (row) {
        var wanted = row.boxes.map(function (b) {
          var others = neighbours(b.id).filter(function (n) { return rowOf.get(n) !== rowOf.get(b.id); });
          return others.length ? d3.mean(others, function (n) { return centre(byId.get(n)); }) : centre(b);
        });
        pack(row.boxes, wanted);
      });
    }

    // 5. Modules with no relationships: a grid under everything else, in module order.
    if (loose.length) {
      var top = rows.length ? y + LAYER_GAP : 0;
      wrap(loose).forEach(function (row) {
        row.forEach(function (b) { b.y = top; });
        pack(row, row.map(function () { return 0; }));
        top += d3.max(row, function (b) { return b.height; }) + ROW_GAP;
      });
    }
  }

  // Tarjan's algorithm: groups of modules that can each reach all the others by following uses. Returns the groups
  // with users before what they use, each group's modules in module order.
  function stronglyConnected(ids, uses) {
    var counter = 0, stack = [], onStack = new Set(), indexOf = new Map(), low = new Map(), found = [];
    function connect(v) {
      indexOf.set(v, counter); low.set(v, counter); counter++;
      stack.push(v); onStack.add(v);
      uses.get(v).forEach(function (w) {
        if (!indexOf.has(w)) { connect(w); low.set(v, Math.min(low.get(v), low.get(w))); }
        else if (onStack.has(w)) low.set(v, Math.min(low.get(v), indexOf.get(w)));
      });
      if (low.get(v) !== indexOf.get(v)) return;
      var group = [], w;
      do { w = stack.pop(); onStack.delete(w); group.push(w); } while (w !== v);
      found.push(group.sort(function (a, b) { return moduleIndex.get(a) - moduleIndex.get(b); }));
    }
    ids.forEach(function (v) { if (!indexOf.has(v)) connect(v); });
    return found.reverse(); // Tarjan finds a group only after everything it uses
  }

  // Places a row's boxes in order, each as near as it can get to its wanted centre without overlapping the one
  // before, then shifts the whole row so it's off from what was wanted by as little as possible on average.
  function pack(row, wanted) {
    var x = -Infinity;
    row.forEach(function (b, i) {
      b.x = Math.max(wanted[i] - b.width / 2, x);
      x = b.x + b.width + GAP_X;
    });
    var shift = d3.mean(row, function (b, i) { return wanted[i] - (b.x + b.width / 2); });
    row.forEach(function (b) { b.x = Math.round(b.x + shift); });
  }

  // ---- Edges ----
  //
  // An edge leaves the side of its box that faces the other box, and arrives at the opposite side of that one.
  // Ends that share a side are spread along it in the order of the boxes at their other ends, so arrows don't
  // land on top of each other. The path is a curve that leaves and arrives square to the sides.

  var OPPOSITE = { top: "bottom", bottom: "top", left: "right", right: "left" };
  var NORMAL = { top: [0, -1], bottom: [0, 1], left: [-1, 0], right: [1, 0] };

  function facingSide(a, b) {
    if (b.y >= a.y + a.height + 4) return "bottom";
    if (b.y + b.height <= a.y - 4) return "top";
    return b.x + b.width / 2 >= a.x + a.width / 2 ? "right" : "left";
  }

  function routeEdges(edges) {
    var ends = new Map(); // "box|side" -> the edge ends on that side
    function addEnd(box, side, other, edge, which) {
      var key = box.id + "|" + side;
      if (!ends.has(key)) ends.set(key, []);
      ends.get(key).push({ box: box, side: side, other: other, edge: edge, which: which });
    }
    edges.forEach(function (e) {
      var side = facingSide(e.source, e.target);
      addEnd(e.source, side, e.target, e, "start");
      addEnd(e.target, OPPOSITE[side], e.source, e, "end");
    });
    ends.forEach(function (list) {
      var horizontal = list[0].side === "top" || list[0].side === "bottom";
      list.sort(function (a, b) {
        return horizontal ? a.other.x + a.other.width / 2 - (b.other.x + b.other.width / 2)
          : a.other.y + a.other.height / 2 - (b.other.y + b.other.height / 2);
      });
      list.forEach(function (end, i) {
        var t = list.length === 1 ? 0.5 : 0.15 + 0.7 * i / (list.length - 1);
        var b = end.box;
        var point = end.side === "top" ? [b.x + t * b.width, b.y]
          : end.side === "bottom" ? [b.x + t * b.width, b.y + b.height]
          : end.side === "left" ? [b.x, b.y + t * b.height] : [b.x + b.width, b.y + t * b.height];
        end.edge[end.which] = { point: point, normal: NORMAL[end.side] };
      });
    });
    edges.forEach(function (e) {
      var p = e.start.point, q = e.end.point;
      var reach = Math.max(24, Math.min(140, Math.hypot(q[0] - p[0], q[1] - p[1]) * 0.4));
      var c1 = [p[0] + e.start.normal[0] * reach, p[1] + e.start.normal[1] * reach];
      var c2 = [q[0] + e.end.normal[0] * reach, q[1] + e.end.normal[1] * reach];
      e.path = "M" + p + "C" + c1 + " " + c2 + " " + q;
      e.middle = [(p[0] + 3 * c1[0] + 3 * c2[0] + q[0]) / 8, (p[1] + 3 * c1[1] + 3 * c2[1] + q[1]) / 8];
    });
  }

  var EDGE_VERBS = { realization: " implements ", association: " holds a ", dependency: " uses " };

  // ---- Drawing ----

  var drawn = null; // { boxes, byId, edges } on screen
  var stale = true; // the diagram needs drawing next time the view is shown

  // The module to focus on: the selected one, when the selection is focused.
  function wantedFocus() { return vb.focused() ? selectedModule() : null; }

  function render() {
    stale = false;
    state.focusModule = wantedFocus();
    var boxes = shownModules().map(buildBox);
    var byId = new Map(boxes.map(function (b) { return [b.id, b]; }));
    var edges = shownEdges().filter(function (e) { return byId.has(e.from) && byId.has(e.to); }).map(function (e) {
      return { from: e.from, to: e.to, kind: e.kind, label: e.label, source: byId.get(e.from), target: byId.get(e.to) };
    });
    layout(boxes, edges);
    if (remembersMoves()) {
      var moved = loadJson(positionsKey()) || {};
      boxes.forEach(function (b) {
        var p = moved[b.id];
        if (p && isFinite(p[0]) && isFinite(p[1])) { b.x = p[0]; b.y = p[1]; b.moved = true; }
      });
    }
    drawn = { boxes: boxes, byId: byId, edges: edges };
    drawBoxes();
    drawEdges();
    if (state.resetView) { state.resetView = false; startingView(); }
    highlight();
    bringSelectionIntoView(false);
  }

  function drawBoxes() {
    var groups = boxLayer.selectAll("g.class-box").data(drawn.boxes, function (b) { return b.id; })
      .join("g")
      .attr("class", function (b) { return "class-box kind-" + b.kind; })
      .attr("data-module", function (b) { return b.id; })
      .attr("transform", function (b) { return "translate(" + b.x + "," + b.y + ")"; })
      .call(dragBehaviour());
    groups.each(function (b) { fillBox(d3.select(this), b); });
  }

  function fillBox(g, b) {
    g.selectAll("*").remove();
    // Tooltips for text that was cut short go on the text's group, not inside the text, so the text's own
    // content stays exactly what's shown.
    g.append("rect").attr("class", "box-outline").attr("width", b.width).attr("height", b.height).attr("rx", 3);
    var header = g.append("g").attr("class", "box-header");
    header.append("text").attr("class", "stereotype").attr("x", b.width / 2).attr("y", PAD_Y + LINE_HEIGHT * 0.75)
      .attr("text-anchor", "middle").attr("font-size", STEREO_SIZE).text(b.stereotype);
    var name = fitText(b.id, b.width - 2 * PAD_X, NAME_SIZE, "600");
    header.append("text").attr("class", "box-name").attr("x", b.width / 2).attr("y", PAD_Y + LINE_HEIGHT * 1.75)
      .attr("text-anchor", "middle").attr("font-size", NAME_SIZE).text(name);
    if (name !== b.id) header.append("title").text(b.id);
    if (!state.show.members) return;

    var y = HEADER_HEIGHT;
    [b.attributes, b.methods].forEach(function (list) {
      g.append("line").attr("class", "divider").attr("x1", 0).attr("x2", b.width).attr("y1", y).attr("y2", y);
      if (!list.length) { y += EMPTY_COMPARTMENT; return; }
      y += PAD_Y;
      list.forEach(function (line) {
        var row = g.append("g").attr("class", "class-line").attr("data-member", line.member).attr("transform", "translate(0," + y + ")");
        row.append("rect").attr("class", "line-hit").attr("x", 1).attr("width", b.width - 2).attr("height", LINE_HEIGHT);
        var shown = fitText(line.text, b.width - 2 * PAD_X, FONT_SIZE);
        row.append("text").attr("x", PAD_X).attr("y", LINE_HEIGHT * 0.72).attr("font-size", FONT_SIZE).text(shown);
        if (shown !== line.text) row.append("title").text(line.text);
        y += LINE_HEIGHT;
      });
      y += PAD_Y;
    });
  }

  function drawEdges() {
    routeEdges(drawn.edges);
    edgeLayer.selectAll("path.class-edge").data(drawn.edges)
      .join(function (enter) { var p = enter.append("path"); p.append("title"); return p; })
      .attr("class", function (e) { return "class-edge " + e.kind; })
      .attr("data-from", function (e) { return e.from; })
      .attr("data-to", function (e) { return e.to; })
      .attr("marker-end", function (e) { return "url(#classes-arrow-" + e.kind + ")"; })
      .attr("d", function (e) { return e.path; })
      .select("title").text(function (e) { return e.from + EDGE_VERBS[e.kind] + e.to + (e.label ? " (" + e.label + ")" : ""); });
    labelLayer.selectAll("text.class-edge-label").data(drawn.edges.filter(function (e) { return e.label; }))
      .join("text")
      .attr("class", "class-edge-label")
      .attr("text-anchor", "middle")
      .attr("font-size", STEREO_SIZE)
      .attr("x", function (e) { return e.middle[0]; })
      .attr("y", function (e) { return e.middle[1] + 4; })
      .text(function (e) { return e.label; });
    applyLegend(); // the lines were just redrawn
  }

  // ---- Dragging ----
  //
  // As in the graph, a press that moves less than the click distance is a click, and leaves the box where it is.

  var dragging = false;

  function dragBehaviour() {
    var pendingX = 0, pendingY = 0;
    return d3.drag()
      .clickDistance(CLICK_DISTANCE)
      .on("start", function () { pendingX = 0; pendingY = 0; })
      .on("drag", function (event, b) {
        if (!dragging) {
          pendingX += event.dx;
          pendingY += event.dy;
          // The event is in diagram units and the click distance is in screen pixels.
          if (Math.hypot(pendingX, pendingY) * d3.zoomTransform(svg.node()).k <= CLICK_DISTANCE) return;
          dragging = true;
          svg.classed("dragging", true);
          b.x += pendingX;
          b.y += pendingY;
        } else {
          b.x += event.dx;
          b.y += event.dy;
        }
        d3.select(this).attr("transform", "translate(" + b.x + "," + b.y + ")");
        drawEdges();
      })
      .on("end", function (event, b) {
        if (!dragging) return; // a click
        dragging = false;
        svg.classed("dragging", false);
        b.x = Math.round(b.x);
        b.y = Math.round(b.y);
        b.moved = true;
        d3.select(this).attr("transform", "translate(" + b.x + "," + b.y + ")");
        drawEdges();
        if (remembersMoves()) rememberPositions();
      });
  }

  function rememberPositions() {
    var moved = loadJson(positionsKey()) || {};
    drawn.boxes.forEach(function (b) { if (b.moved) moved[b.id] = [b.x, b.y]; });
    saveJson(positionsKey(), moved);
  }

  // ---- Zoom ----
  //
  // Like the graph: the wheel zooms around the pointer and dragging the background pans.

  var ZOOM_STEP = 1.25;
  var zoom = d3.zoom().scaleExtent([0.05, 4]).on("zoom", function (event) { viewport.attr("transform", event.transform); });
  svg.call(zoom).on("dblclick.zoom", null);

  function bounds(boxes) {
    return {
      x0: d3.min(boxes, function (b) { return b.x; }), x1: d3.max(boxes, function (b) { return b.x + b.width; }),
      y0: d3.min(boxes, function (b) { return b.y; }), y1: d3.max(boxes, function (b) { return b.y + b.height; })
    };
  }

  // The space the diagram can use: below the toolbar, which floats over it and can wrap onto two rows, and above
  // the legend while it's open.
  var legend = document.getElementById("classes-legend");
  function room() {
    var top = view.querySelector(".view-toolbar").offsetHeight + 16;
    var bottom = legend.open ? legend.offsetHeight + 24 : 16;
    return { top: top, width: view.clientWidth || 800, height: Math.max(100, (view.clientHeight || 600) - top - bottom) };
  }

  // The starting view fits the diagram, but not below 40%, so names stay readable. A diagram that doesn't fit at
  // 40% starts at its top, and at its left if it's wider than the view too.
  function startingView() {
    if (!drawn || !drawn.boxes.length) return;
    var b = bounds(drawn.boxes), r = room();
    var w = b.x1 - b.x0, h = b.y1 - b.y0;
    var k = Math.max(0.4, Math.min(1, (r.width - 32) / w, r.height / h));
    var x = w * k <= r.width - 32 ? (r.width - w * k) / 2 : 16;
    var y = h * k <= r.height ? r.top + (r.height - h * k) / 2 : r.top;
    svg.call(zoom.transform, d3.zoomIdentity.translate(x, y).scale(k).translate(-b.x0, -b.y0));
  }

  function fit() {
    if (!drawn || !drawn.boxes.length) return;
    var b = bounds(drawn.boxes), r = room();
    var k = Math.min(2, (r.width - 32) / (b.x1 - b.x0), r.height / (b.y1 - b.y0));
    svg.transition().duration(300).call(zoom.transform, d3.zoomIdentity
      .translate(r.width / 2, r.top + r.height / 2).scale(k).translate(-(b.x0 + b.x1) / 2, -(b.y0 + b.y1) / 2));
  }

  document.getElementById("classes-zoom-in").addEventListener("click", function () { svg.transition().duration(200).call(zoom.scaleBy, ZOOM_STEP); });
  document.getElementById("classes-zoom-out").addEventListener("click", function () { svg.transition().duration(200).call(zoom.scaleBy, 1 / ZOOM_STEP); });
  document.getElementById("classes-fit").addEventListener("click", fit);

  // ---- Selection ----

  function selectedModule() {
    var selection = vb.selection();
    if (!selection) return null;
    var member = vb.membersById.get(selection);
    return member ? member.module : (vb.modulesById.has(selection) ? selection : null);
  }

  function highlight() {
    if (!drawn) return;
    var selection = vb.selection();
    var moduleId = selectedModule();
    boxLayer.selectAll("g.class-box").classed("selected", function (b) { return b.id === moduleId; });
    boxLayer.selectAll("g.class-line").classed("selected-line", function () { return isSameMember(this.getAttribute("data-member"), selection); });
    applyLegend();
    updateToolbar();
  }

  // A property's line stands for all its accessors, so selecting the Let shows the line too.
  function isSameMember(lineId, selection) {
    if (!selection || lineId === selection) return lineId === selection;
    var a = vb.membersById.get(lineId);
    var b = vb.membersById.get(selection);
    return !!a && !!b && a.module === b.module && a.name.toLowerCase() === b.name.toLowerCase() &&
      /^property/.test(a.kind) && /^property/.test(b.kind);
  }

  // Moves the selected box into view if any of it is out of sight: all of it, or its top if it's taller than the view.
  function bringSelectionIntoView(animate) {
    var box = drawn && drawn.byId.get(selectedModule());
    if (!box) return;
    var t = d3.zoomTransform(svg.node()), r = room();
    var height = Math.min(box.height, r.height / t.k);
    var left = t.applyX(box.x), right = t.applyX(box.x + box.width);
    var top = t.applyY(box.y), bottom = t.applyY(box.y + height);
    if (left >= 0 && right <= r.width && top >= r.top && bottom <= r.top + r.height) return;
    (animate ? svg.transition().duration(300) : svg)
      .call(zoom.translateTo, box.x + box.width / 2, box.y + height / 2, [r.width / 2, r.top + r.height / 2]);
  }

  var clickedHere = false; // a selection made by clicking in the diagram needs no moving

  svg.on("click", function (event) {
    var line = event.target.closest("g.class-line");
    var box = event.target.closest("g.class-box");
    clickedHere = true;
    var other = event.ctrlKey || event.metaKey;
    if (line) vb.select(line.getAttribute("data-member"), other);
    else if (box) vb.select(box.getAttribute("data-module"), other);
    else if (event.target === svg.node()) clearHighlights();
    clickedHere = false;
  });

  // Clicking the background, or Escape, clears the legend's highlight and the selection, as in the graph.
  function clearHighlights() {
    state.legend.clear();
    vb.select(null);
    highlight();
  }

  // Escape also closes the drop-downs (see page.js), so it only clears when none of them is open, and not while typing
  // in a field. This runs in the capture phase, before page.js closes them.
  document.addEventListener("keydown", function (event) {
    if (event.key !== "Escape" || vb.activeView() !== "classes" || !drawn) return;
    if (document.querySelector("details.dropdown[open]")) return;
    if (event.target.closest && event.target.closest("input, select, textarea")) return;
    clearHighlights();
  }, true);

  // ---- Toolbar ----

  function updateToolbar() {
    focusButton.setAttribute("aria-pressed", vb.focusOnClick() ? "true" : "false");
    filterMenu.querySelectorAll("input[data-option]").forEach(function (input) {
      input.checked = state.show[input.getAttribute("data-option")];
    });
  }

  // Focus is the page's Focus on click setting, shared by every view; turning it on focuses on the selection now too.
  focusButton.addEventListener("click", function () { vb.setFocusOnClick(!vb.focusOnClick()); });

  document.getElementById("classes-relayout").addEventListener("click", function () {
    if (remembersMoves()) saveJson(positionsKey(), null);
    state.resetView = true;
    render();
  });

  filterMenu.innerHTML = OPTIONS.map(function (o) {
    return "<label><input type=\"checkbox\" data-option=\"" + o.key + "\"> " + o.label + "</label>";
  }).join("");
  filterMenu.addEventListener("change", function (event) {
    var key = event.target.getAttribute("data-option");
    state.show[key] = event.target.checked;
    saveSetting("vbadoc-classes-" + key, event.target.checked);
    render();
  });

  // ---- Legend ----
  //
  // As in the graph, each entry is a toggle that lights up what it stands for and fades the rest: a kind of box lights
  // those boxes and the lines between them, and a relationship lights those lines and the boxes at their ends. Entries
  // add to each other, and Clear, the background or Escape turns them off.

  var BOX_KINDS = [
    ["module", "Standard module"], ["class", "Class"], ["interface", "Interface"], ["form", "UserForm"], ["document", "Document module"]
  ];
  var RELATIONSHIPS = [["realization", "Implements"], ["association", "Holds in a variable (labelled)"], ["dependency", "Uses"]];

  function boxIcon(kind) {
    return "<svg width=\"18\" height=\"14\" viewBox=\"0 0 18 14\" aria-hidden=\"true\"><g class=\"class-box kind-" + kind + "\">" +
      "<rect class=\"box-outline\" x=\"1\" y=\"1\" width=\"16\" height=\"12\" rx=\"2\"></rect></g></svg>";
  }

  function arrow(kind) {
    return "<svg width=\"36\" height=\"12\" viewBox=\"0 -6 36 12\" aria-hidden=\"true\">" +
      "<path class=\"class-edge " + kind + "\" d=\"M1,0 L28,0\" marker-end=\"url(#classes-arrow-" + kind + ")\"></path></svg>";
  }

  function legendItem(icon, text, key) {
    return "<li><button type=\"button\" class=\"legend-item\" data-legend=\"" + key + "\" aria-pressed=\"false\">" +
      icon + "<span>" + text + "</span></button></li>";
  }

  var legendBody = document.getElementById("classes-legend-body");
  legendBody.innerHTML =
    "<p class=\"legend-hint muted\">Click an item to highlight it." +
    " <button type=\"button\" class=\"link-button\" id=\"classes-legend-clear\" hidden>Clear</button></p>" +
    "<h3>Boxes</h3><ul>" + BOX_KINDS.map(function (k) { return legendItem(boxIcon(k[0]), k[1], "box:" + k[0]); }).join("") + "</ul>" +
    "<h3>Relationships</h3><ul>" + RELATIONSHIPS.map(function (r) { return legendItem(arrow(r[0]), r[1], "edge:" + r[0]); }).join("") + "</ul>" +
    "<p class=\"muted\" style=\"margin:4px 0 0\">+ public, − private, ~ friend. Click a box or a line to see its source; drag a box to move it.</p>";

  // What the clicked entries light up, or null when none is clicked.
  function legendLit() {
    if (state.legend.size === 0 || !drawn) return null;
    var kinds = new Set();
    var relationships = new Set();
    state.legend.forEach(function (key) {
      if (key.indexOf("box:") === 0) kinds.add(key.slice(4)); else relationships.add(key.slice(5));
    });
    var boxes = new Set(drawn.boxes.filter(function (b) { return kinds.has(b.kind); }).map(function (b) { return b.id; }));
    var edges = new Set(drawn.edges.filter(function (e) {
      return relationships.has(e.kind) || (boxes.has(e.from) && boxes.has(e.to));
    }));
    edges.forEach(function (e) { boxes.add(e.from); boxes.add(e.to); });
    return { boxes: boxes, edges: edges };
  }

  function applyLegend() {
    if (!drawn) return;
    var lit = legendLit();
    boxLayer.selectAll("g.class-box").classed("dim", function (b) { return lit !== null && !lit.boxes.has(b.id); });
    edgeLayer.selectAll("path.class-edge").classed("dim", function (e) { return lit !== null && !lit.edges.has(e); });
    labelLayer.selectAll("text.class-edge-label").classed("dim", function (e) { return lit !== null && !lit.edges.has(e); });
    legendBody.querySelectorAll("[data-legend]").forEach(function (button) {
      button.setAttribute("aria-pressed", state.legend.has(button.getAttribute("data-legend")) ? "true" : "false");
    });
    document.getElementById("classes-legend-clear").hidden = state.legend.size === 0;
  }

  legendBody.addEventListener("click", function (event) {
    if (event.target.closest("#classes-legend-clear")) { state.legend.clear(); applyLegend(); return; }
    var button = event.target.closest("[data-legend]");
    if (!button) return;
    var key = button.getAttribute("data-legend");
    if (state.legend.has(key)) state.legend.delete(key); else state.legend.add(key);
    applyLegend();
  });

  // ---- Wiring to the rest of the page ----

  function drawIfNeeded() {
    if (vb.activeView() !== "classes") return;
    if (stale || !drawn) render(); else { highlight(); bringSelectionIntoView(false); }
  }

  vb.onViewChange(drawIfNeeded);

  // A different module to focus on, or none, is a different diagram: it starts from its starting view.
  function refocusIfNeeded() {
    if (wantedFocus() === state.focusModule) return false;
    state.resetView = true;
    if (vb.activeView() === "classes") render(); else stale = true;
    return true;
  }

  vb.onFocusChange(function () { if (!refocusIfNeeded()) updateToolbar(); });

  // A different filter is a different diagram.
  vb.onFilterChange(function () {
    state.resetView = true;
    if (vb.activeView() === "classes") render(); else stale = true;
  });

  vb.onSelectionChange(function () {
    if (refocusIfNeeded()) return;
    highlight();
    // A box clicked here is already in view, unless the side panel has just opened over it.
    if ((!clickedHere || vb.panelFollowing()) && vb.activeView() === "classes") bringSelectionIntoView(true);
  });

  // Colours come from the page's CSS variables, so a theme change needs no redrawing.

  window.vbadocClasses = {
    // For tests: whether a drawing is on screen, whether the view is still sliding or zooming into place, the
    // zoom, and a box's top left corner in diagram units.
    drawn: function () { return !!drawn && !stale; },
    moving: function () { return d3.active(svg.node()) !== null; },
    zoom: function () { return d3.zoomTransform(svg.node()).k; },
    position: function (id) { var b = drawn && drawn.byId.get(id); return b ? { x: b.x, y: b.y } : null; }
  };

  updateToolbar();
  drawIfNeeded();
})();
