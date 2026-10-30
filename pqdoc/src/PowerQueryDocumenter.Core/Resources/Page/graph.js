// graph.js: the dependency graph view, drawn with d3.
//
// Queries sit in columns by depth, so data flows left to right, and d3's force layout arranges them within those
// columns. Clicking a query selects it through window.pqdoc, which shows its details; the graph then traces
// everything upstream and downstream of it. Hovering does the same for a query's direct neighbours, and the search
// box lights up the queries it matches.
//
// The starting layout is computed in one go before anything is drawn, from fixed positions, so the same queries
// always give the same first picture. After that the force simulation is kept: dragging a query pins it where it's
// dropped and wakes the simulation up, so the rest of the graph rearranges around it.
(function () {
  "use strict";

  var pq = window.pqdoc;
  var data = pq.data;
  var reducedMotion = !!window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var LABEL_LIMIT = 28;
  var COLUMN_WIDTH = 210;
  var RADIUS = 8;

  var nodes = data.queries.map(function (q) { return Object.assign({}, q); });
  var links = data.edges.map(function (e) { return { source: e.from, target: e.to, inCycle: e.inCycle }; });

  // ---- Elements ----

  var view = document.getElementById("view-graph");
  var toolbar = view.querySelector(".view-toolbar");
  var legend = document.getElementById("graph-legend");
  var tooltip = document.getElementById("graph-tooltip");
  var svg = d3.select("#graph-svg");
  var defs = svg.append("defs");
  var viewport = svg.append("g").attr("class", "viewport");
  var guideLayer = viewport.append("g").attr("class", "guides").attr("aria-hidden", "true");
  var edgeLayer = viewport.append("g").attr("class", "edges").attr("aria-hidden", "true");
  var nodeLayer = viewport.append("g").attr("class", "nodes");

  ["arrow", "arrow-cycle", "arrow-upstream", "arrow-downstream"].forEach(function (id) {
    defs.append("marker")
      .attr("id", id)
      .attr("viewBox", "0 -4 9 8")
      .attr("refX", 9)
      .attr("refY", 0)
      .attr("markerWidth", 9)
      .attr("markerHeight", 8)
      .attr("markerUnits", "userSpaceOnUse")
      .attr("orient", "auto")
      .append("path")
      .attr("class", id)
      .attr("d", "M0,-4L9,0L0,4Z");
  });

  // ---- Layout ----

  // Each query gets a column by its depth from the roots (the longest chain of queries that feed it), so data flows
  // left to right like stages on a line. Isolated queries sit in their own column on the far left. The queries of a
  // circular dependency are treated as one unit and share a column, which keeps the depth calculation finite.
  function assignColumns() {
    var groupOf = new Map();
    nodes.forEach(function (n) { groupOf.set(n.id, "n" + n.id); });
    data.cycles.forEach(function (members, index) {
      members.forEach(function (id) { groupOf.set(id, "c" + index); });
    });

    var groups = Array.from(new Set(groupOf.values()));
    var next = new Map(groups.map(function (g) { return [g, new Set()]; }));
    var remaining = new Map(groups.map(function (g) { return [g, 0]; }));
    links.forEach(function (l) {
      var from = groupOf.get(l.source);
      var to = groupOf.get(l.target);
      if (from !== to && !next.get(from).has(to)) {
        next.get(from).add(to);
        remaining.set(to, remaining.get(to) + 1);
      }
    });

    var depth = new Map(groups.map(function (g) { return [g, 0]; }));
    var queue = groups.filter(function (g) { return remaining.get(g) === 0; });
    while (queue.length > 0) {
      var group = queue.shift();
      next.get(group).forEach(function (to) {
        depth.set(to, Math.max(depth.get(to), depth.get(group) + 1));
        remaining.set(to, remaining.get(to) - 1);
        if (remaining.get(to) === 0) queue.push(to);
      });
    }

    nodes.forEach(function (n, index) {
      n.column = n.role === "isolated" ? -1 : depth.get(groupOf.get(n.id));
      n.x = n.column * COLUMN_WIDTH;
      n.y = ((index * 0.618) % 1 - 0.5) * 400; // an even, fixed spread down the column
    });
  }

  assignColumns();

  var simulation = d3.forceSimulation(nodes)
    .force("link", d3.forceLink(links).id(function (n) { return n.id; }).distance(110).strength(0.25))
    .force("charge", d3.forceManyBody().strength(-240).distanceMax(600))
    .force("column", d3.forceX(function (n) { return n.column * COLUMN_WIDTH; }).strength(0.7))
    .force("centre", d3.forceY(0).strength(0.05))
    .force("collide", d3.forceCollide(RADIUS + 14))
    .stop();

  // ---- Drawing ----

  var columns = Array.from(new Set(nodes.map(function (n) { return n.column; }))).sort(function (a, b) { return a - b; });
  var guide = guideLayer.selectAll("g").data(columns).join("g").attr("class", "guide");
  guide.append("line");
  guide.append("text").text(function (c) { return c === -1 ? "Isolated" : "Depth " + c; });

  // Two queries that reference each other get two curved arrows instead of one overlapping line.
  var pairs = new Set(links.map(function (l) { return l.source.id + "\u0000" + l.target.id; }));
  links.forEach(function (l) { l.curved = pairs.has(l.target.id + "\u0000" + l.source.id); });

  var link = edgeLayer.selectAll("path").data(links).join("path")
    .attr("class", function (l) { return "edge" + (l.inCycle ? " cycle" : ""); })
    .attr("data-from", function (l) { return l.source.id; })
    .attr("data-to", function (l) { return l.target.id; });

  var node = nodeLayer.selectAll("g").data(nodes, function (n) { return n.id; }).join("g")
    .attr("class", function (n) { return "node " + n.role; })
    .attr("data-id", function (n) { return n.id; })
    .attr("tabindex", 0)
    .attr("role", "button")
    .attr("aria-label", function (n) { return n.id + ": " + describeCounts(n); });

  node.append("circle").attr("class", "hit").attr("r", RADIUS + 6);
  node.append("circle").attr("class", "halo").attr("r", RADIUS + 7);
  node.filter(function (n) { return n.inCycle; }).append("circle").attr("class", "ring").attr("r", RADIUS + 4);
  node.each(function (n) {
    var g = d3.select(this);
    if (n.role === "leaf") {
      g.append("rect").attr("class", "shape").attr("x", -7.5).attr("y", -7.5).attr("width", 15).attr("height", 15).attr("rx", 2);
    } else {
      g.append("circle").attr("class", "shape").attr("r", n.role === "root" ? RADIUS : RADIUS - 1);
    }
    if (n.role === "root") g.append("circle").attr("class", "core").attr("r", 3);
  });
  // Edges leave a query to the right, so its name sits close enough for the name's outline to meet the mark.
  // Otherwise each edge shows in the gap, and reads as a dash in front of the name.
  node.append("text")
    .attr("class", "label")
    .attr("x", function (n) { return n.inCycle ? RADIUS + 7 : RADIUS + 3; })
    .attr("dy", "0.32em")
    .text(function (n) { return truncate(n.id); });

  function truncate(name) {
    return name.length > LABEL_LIMIT ? name.slice(0, LABEL_LIMIT - 1) + "…" : name;
  }

  function describeCounts(n) {
    return "uses " + pq.countText(n.upstream) + ", used by " + pq.countText(n.downstream);
  }

  // Moves everything to the queries' current positions: after the starting layout and on every tick.
  function updatePositions() {
    link.attr("d", function (l) {
      var dx = l.target.x - l.source.x;
      var dy = l.target.y - l.source.y;
      var length = Math.sqrt(dx * dx + dy * dy) || 1;
      var ux = dx / length;
      var uy = dy / length;
      // Curved arrows bend to the left of their direction, so a pair never overlaps.
      var bend = l.curved ? 0.35 : 0;
      var nx = -uy * bend;
      var ny = ux * bend;
      var trimStart = RADIUS + 1;
      var trimEnd = RADIUS + (l.target.inCycle ? 6 : 3);
      var x1 = l.source.x + (ux + nx) * trimStart;
      var y1 = l.source.y + (uy + ny) * trimStart;
      var x2 = l.target.x - (ux - nx) * trimEnd;
      var y2 = l.target.y - (uy - ny) * trimEnd;
      if (!l.curved) return "M" + x1 + "," + y1 + " L" + x2 + "," + y2;
      var cx = (l.source.x + l.target.x) / 2 + nx * length * 0.6;
      var cy = (l.source.y + l.target.y) / 2 + ny * length * 0.6;
      return "M" + x1 + "," + y1 + " Q" + cx + "," + cy + " " + x2 + "," + y2;
    });

    node.attr("transform", function (n) { return "translate(" + n.x + "," + n.y + ")"; });

    var extent = d3.extent(nodes, function (n) { return n.y; });
    var top = (extent[0] || 0) - 60;
    var bottom = (extent[1] || 0) + 40;
    guide.select("line")
      .attr("x1", function (c) { return c * COLUMN_WIDTH; })
      .attr("x2", function (c) { return c * COLUMN_WIDTH; })
      .attr("y1", top + 12)
      .attr("y2", bottom);
    guide.select("text")
      .attr("x", function (c) { return c * COLUMN_WIDTH; })
      .attr("y", top);
  }

  // ---- Pan, zoom and fit ----

  // Once the person pans or zooms, the view is theirs: automatic refitting stops until Reset view.
  var userMovedView = false;
  var zoom = d3.zoom()
    .scaleExtent([0.05, 6])
    .on("zoom", function (event) {
      viewport.attr("transform", event.transform);
      if (event.sourceEvent) userMovedView = true;
      hideTooltip();
    });
  svg.call(zoom).on("dblclick.zoom", null);
  svg.on("click", function (event) { if (event.target === svg.node()) pq.select(null); });

  function moveTo(transform, duration) {
    (duration && !reducedMotion ? svg.transition().duration(duration) : svg).call(zoom.transform, transform);
  }

  // Fits the graph into the space between the toolbar, which can wrap, and the open legend.
  function fit(duration) {
    var box = viewport.node().getBBox();
    if (!box.width && !box.height) return;
    var width = view.clientWidth || 800;
    var height = view.clientHeight || 600;
    var top = toolbar.offsetHeight + 16;
    var bottom = legend.open ? legend.offsetHeight + 16 : 0;
    var room = Math.max(height - top - bottom, height * 0.5);
    var padding = 24;
    var scale = Math.min(2, (width - padding * 2) / Math.max(box.width, 1), (room - padding * 2) / Math.max(box.height, 1));
    moveTo(d3.zoomIdentity
      .translate(width / 2, top + room / 2)
      .scale(Math.max(scale, 0.05))
      .translate(-(box.x + box.width / 2), -(box.y + box.height / 2)), duration);
  }

  function centreOn(n) {
    var k = Math.max(d3.zoomTransform(svg.node()).k, 1);
    moveTo(d3.zoomIdentity.translate(view.clientWidth / 2, view.clientHeight / 2).scale(k).translate(-n.x, -n.y), 400);
  }

  legend.addEventListener("toggle", function () { if (started && !userMovedView) fit(300); });
  simulation.on("end", function () { if (!userMovedView) fit(400); });

  // When the view changes size (the details panel is resized or hidden, or the window), refit it, or once the view
  // is the person's own, keep what was in the middle in the middle. A hidden view has no size, so changes while
  // it's hidden are skipped.
  var lastSize = null;
  if (window.ResizeObserver) {
    new ResizeObserver(function () {
      var width = view.clientWidth;
      var height = view.clientHeight;
      if (width === 0 || height === 0) { lastSize = null; return; }
      if (started && lastSize && !userMovedView) {
        fit(0);
      } else if (started && lastSize) {
        var k = d3.zoomTransform(svg.node()).k;
        svg.call(zoom.translateBy, (width - lastSize.width) / 2 / k, (height - lastSize.height) / 2 / k);
      }
      lastSize = { width: width, height: height };
    }).observe(view);
  }

  // ---- Dragging ----
  //
  // Dragging a query pins it where it's dropped, and the rest of the graph rearranges around it. A double-click
  // lets it go. A small move is still a click, so clicking to select neither pins a query nor wakes the simulation.

  var CLICK_DISTANCE = 4;
  var moved = false;

  node.call(d3.drag()
    .clickDistance(CLICK_DISTANCE)
    .on("start", function () { moved = false; })
    .on("drag", function (event, n) {
      if (!moved) {
        // The event is in graph units and the click distance is in screen pixels.
        var distance = Math.hypot(event.x - n.x, event.y - n.y) * d3.zoomTransform(svg.node()).k;
        if (distance <= CLICK_DISTANCE) return;
        moved = true;
        hideTooltip();
        simulation.alphaTarget(0.2).restart();
      }
      n.fx = event.x;
      n.fy = event.y;
    })
    .on("end", function () {
      if (!moved) return;
      simulation.alphaTarget(0);
      d3.select(this).classed("pinned", true);
    }));

  node.on("dblclick", function (event, n) {
    event.stopPropagation();
    n.fx = null;
    n.fy = null;
    d3.select(this).classed("pinned", false);
    simulation.alpha(0.2).restart();
  });

  // ---- Highlighting ----

  var hoveredId = null;

  // What to light up: a selection's whole chains, or a hovered query's direct neighbours, or the search's matches.
  function highlight() {
    var selection = pq.selection();
    var matches = pq.matches();
    var lit = null;
    var direction = new Map(); // edge -> "upstream" or "downstream"

    if (selection !== null) {
      var up = pq.upstream(selection);
      var down = pq.downstream(selection);
      lit = new Set([selection]);
      up.forEach(function (id) { lit.add(id); });
      down.forEach(function (id) { lit.add(id); });
      links.forEach(function (l) {
        if (up.has(l.source.id) && (l.target.id === selection || up.has(l.target.id))) direction.set(l, "upstream");
        else if (down.has(l.target.id) && (l.source.id === selection || down.has(l.source.id))) direction.set(l, "downstream");
      });
    } else if (hoveredId !== null) {
      lit = new Set([hoveredId]);
      pq.uses(hoveredId).forEach(function (id) { lit.add(id); });
      pq.usedBy(hoveredId).forEach(function (id) { lit.add(id); });
      links.forEach(function (l) {
        if (l.target.id === hoveredId) direction.set(l, "upstream");
        else if (l.source.id === hoveredId) direction.set(l, "downstream");
      });
    } else if (matches !== null) {
      lit = matches;
    }

    node
      .classed("selected", function (n) { return n.id === selection; })
      .classed("related", function (n) { return lit !== null && lit.has(n.id) && n.id !== selection; })
      .classed("match", function (n) { return matches !== null && matches.has(n.id); })
      .classed("dim", function (n) { return lit !== null && !lit.has(n.id); });
    link
      .classed("highlighted", function (l) { return direction.has(l); })
      .classed("upstream", function (l) { return direction.get(l) === "upstream"; })
      .classed("downstream", function (l) { return direction.get(l) === "downstream"; })
      .classed("dim", function (l) { return lit !== null && !direction.has(l); })
      .attr("marker-end", function (l) {
        if (direction.has(l)) return "url(#arrow-" + direction.get(l) + ")";
        return l.inCycle ? "url(#arrow-cycle)" : "url(#arrow)";
      });
  }

  function hoverOver(id) {
    hoveredId = id;
    if (pq.selection() === null) highlight();
  }

  // ---- Selecting ----

  // A query clicked in the graph is already where the person is looking. One picked anywhere else (the search box,
  // the details panel or the keyboard) is centred, so it can be found.
  var clickedInGraph = false;

  node.on("click", function (event, n) {
    event.stopPropagation();
    clickedInGraph = true;
    pq.select(n.id === pq.selection() ? null : n.id);
    clickedInGraph = false;
  });

  node.on("keydown", function (event, n) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      pq.select(n.id);
    }
  });

  // ---- Tooltip ----

  function showTooltip(n, clientX, clientY) {
    tooltip.replaceChildren();
    var name = document.createElement("strong");
    name.textContent = n.id;
    var counts = document.createElement("span");
    counts.textContent = "Uses " + pq.countText(n.upstream) + ", used by " + pq.countText(n.downstream);
    tooltip.append(name, counts);
    if (n.inCycle) {
      var cycle = document.createElement("span");
      cycle.textContent = "Part of a circular dependency";
      tooltip.append(cycle);
    }
    tooltip.hidden = false;
    moveTooltip(clientX, clientY);
  }

  function moveTooltip(clientX, clientY) {
    var box = view.getBoundingClientRect();
    var x = clientX - box.left + 14;
    var y = clientY - box.top + 14;
    if (x + tooltip.offsetWidth > box.width - 8) x = clientX - box.left - tooltip.offsetWidth - 14;
    if (y + tooltip.offsetHeight > box.height - 8) y = clientY - box.top - tooltip.offsetHeight - 14;
    tooltip.style.left = Math.max(4, x) + "px";
    tooltip.style.top = Math.max(4, y) + "px";
  }

  function hideTooltip() { tooltip.hidden = true; }

  node
    .on("pointerenter", function (event, n) {
      hoverOver(n.id);
      showTooltip(n, event.clientX, event.clientY);
    })
    .on("pointermove", function (event) { if (!tooltip.hidden) moveTooltip(event.clientX, event.clientY); })
    .on("pointerleave", function () {
      hoverOver(null);
      hideTooltip();
    })
    .on("focus", function (event, n) {
      hoverOver(n.id);
      var box = this.getBoundingClientRect();
      showTooltip(n, box.right, box.bottom);
    })
    .on("blur", function () {
      hoverOver(null);
      hideTooltip();
    });

  // ---- Reset view ----

  document.getElementById("graph-reset").addEventListener("click", function () {
    pq.clearSearch();
    pq.select(null);
    nodes.forEach(function (n) { n.fx = null; n.fy = null; });
    node.classed("pinned", false);
    userMovedView = false;
    if (!reducedMotion) simulation.alpha(0.3).restart();
    fit(500);
  });

  // ---- Wiring to the rest of the page ----

  var started = false;
  function drawIfVisible() {
    if (pq.activeView() !== "graph") return;
    if (started) { highlight(); return; } // catch up with a selection made in another view
    started = true;
    if (view.clientWidth < 600 || view.clientHeight < 480) legend.open = false; // it would cover a small view
    simulation.tick(300);
    simulation.alpha(0);
    simulation.on("tick", updatePositions);
    updatePositions();
    highlight();
    fit(0);
  }

  pq.onSelectionChange(function (id) {
    if (!started) return;
    hideTooltip();
    highlight();
    // A hidden view has no size, and moving it then would give d3-zoom a NaN transform.
    if (id !== null && !clickedInGraph && pq.activeView() === "graph") {
      centreOn(nodes.find(function (n) { return n.id === id; }));
    }
  });
  pq.onSearchChange(function () { if (started) highlight(); });
  pq.onViewChange(drawIfVisible);
  // Colours are CSS variables, so a theme change needs no redraw.

  window.pqdocGraph = {
    nodeCount: function () { return nodes.length; },
    edgeCount: function () { return links.length; },
    // For tests: whether the simulation is still moving things.
    moving: function () { return simulation.alpha() > simulation.alphaMin(); },
    position: function (id) {
      var n = nodes.find(function (other) { return other.id === id; });
      return n ? { x: n.x, y: n.y, column: n.column } : null;
    }
  };

  drawIfVisible();
})();
