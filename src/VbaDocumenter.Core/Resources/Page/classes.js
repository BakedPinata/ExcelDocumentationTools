// classes.js: the class diagram view, drawn with Mermaid.
//
// One box per module, with its members and a stereotype for its kind; relationships come from the page data
// (realisation, association, dependency). The Mermaid text is generated here from the data, so focus mode and
// the Show… options only need to generate a smaller diagram. Mermaid runs with securityLevel "strict", so the
// diagram has no click directives: clicks are handled on the rendered SVG instead, by mapping each box and
// each member line back to its module or member.
//
// Mermaid details this relies on (checked against 11.17.2):
// - class ids are generated (M1, M2, ...) and the real name is the box label, so no name can break the syntax;
// - inside a class body, a line containing ")" is a method and anything else an attribute, and Mermaid draws
//   all attributes, then all methods, each in order;
// - member text goes through markdown and entity decoding, so characters that mean something to Mermaid or
//   markdown are written as entity codes (#123; for "{"), which Mermaid turns back into the character.
(function () {
  "use strict";

  var vb = window.vbadoc;
  var data = vb.data;

  var STEREOTYPES = { standard: "module", "class": "class", form: "form", document: "document" };
  function stereotype(module) { return module.isInterface ? "interface" : STEREOTYPES[module.kind] || "class"; }

  // ---- Settings (remembered per browser; storage can fail on file://) ----

  function loadSetting(key, fallback) { try { var v = localStorage.getItem(key); return v === null ? fallback : v === "true"; } catch (e) { return fallback; } }
  function saveSetting(key, value) { try { localStorage.setItem(key, String(value)); } catch (e) { /* not remembered */ } }

  var OPTIONS = [
    { key: "members", label: "Members" },
    { key: "private", label: "Private members" },
    { key: "dependencies", label: "Dependencies (other uses)" }
  ];

  var state = {
    focus: false,
    focusModule: null,  // the module the focused diagram was drawn around
    zoom: 1,
    show: {}
  };
  // Dependencies start out unset (null), meaning "automatic": see showDependencies().
  OPTIONS.forEach(function (o) { state.show[o.key] = loadSetting("vbadoc-classes-" + o.key, o.key === "dependencies" ? null : true); });

  // Mermaid's layout slows down sharply with many edges (about a minute for 600 dependencies), so unless
  // the user has chosen, the full diagram leaves dependencies out when there are more than this many.
  // Focus mode, which draws only a neighbourhood, always shows them.
  var AUTO_DEPENDENCY_LIMIT = 150;
  var dependencyCount = data.classEdges.filter(function (e) { return e.kind === "dependency"; }).length;

  function showDependencies() {
    if (state.show.dependencies !== null) return state.show.dependencies;
    return state.focus || dependencyCount <= AUTO_DEPENDENCY_LIMIT;
  }

  // ---- Elements ----

  var view = document.getElementById("view-classes");
  var canvas = document.getElementById("classes-canvas");
  var message = document.getElementById("classes-message");
  var focusButton = document.getElementById("classes-focus");
  var note = document.getElementById("classes-note");
  var filterMenu = document.getElementById("classes-filter-menu");

  // ---- From the page data to Mermaid ----

  var classIdOf = new Map();
  var moduleOfClassId = new Map();
  data.modules.forEach(function (m, i) {
    classIdOf.set(m.id, "M" + (i + 1));
    moduleOfClassId.set("M" + (i + 1), m.id);
  });

  var membersByModule = new Map();
  data.members.forEach(function (m) {
    if (!m.class) return;
    if (!membersByModule.has(m.module)) membersByModule.set(m.module, []);
    membersByModule.get(m.module).push(m);
  });

  // Every character Mermaid or markdown might treat specially, as an entity code. "#" is among them, so an
  // entity code already in the text can't be decoded by accident.
  function escapeText(text) {
    return String(text).replace(/[#{}~<>&$*_\[\]()`]/g, function (c) { return "#" + c.charCodeAt(0) + ";"; });
  }

  // A member's line: an attribute is all escaped text; a method keeps its own brackets as the only real ones.
  function memberLine(line) {
    if (line.parameters == null) return escapeText(line.text);
    return escapeText(line.text) + "(" + escapeText(line.parameters) + ")" + (line.type ? " " + escapeText(line.type) : "");
  }

  function shownEdges() {
    var dependencies = showDependencies();
    return data.classEdges.filter(function (e) { return dependencies || e.kind !== "dependency"; });
  }

  // Which modules the current diagram shows: all of them, or in focus mode the focused module and every
  // module a shown relationship links it with.
  function shownModules() {
    if (!state.focus || !state.focusModule) return data.modules.map(function (m) { return m.id; });
    var ids = new Set([state.focusModule]);
    shownEdges().forEach(function (e) {
      if (e.from === state.focusModule) ids.add(e.to);
      if (e.to === state.focusModule) ids.add(e.from);
    });
    return data.modules.filter(function (m) { return ids.has(m.id); }).map(function (m) { return m.id; });
  }

  // Builds the Mermaid text, and for each box the member ids of its attribute and method lines in drawing order.
  function buildDiagram(moduleIds) {
    var shown = new Set(moduleIds);
    var lines = ["classDiagram"];
    var boxes = new Map();
    moduleIds.forEach(function (id) {
      var module = vb.modulesById.get(id);
      var box = { attributes: [], methods: [] };
      boxes.set(id, box);
      lines.push("class " + classIdOf.get(id) + "[\"" + escapeText(id) + "\"] {");
      lines.push("  <<" + stereotype(module) + ">>");
      if (state.show.members) {
        (membersByModule.get(id) || []).forEach(function (m) {
          if (!state.show["private"] && m.class.text.charAt(0) === "-") return;
          lines.push("  " + memberLine(m.class));
          (m.class.parameters == null ? box.attributes : box.methods).push(m.id);
        });
      }
      lines.push("}");
    });
    shownEdges().forEach(function (e) {
      if (!shown.has(e.from) || !shown.has(e.to)) return;
      var from = classIdOf.get(e.from);
      var to = classIdOf.get(e.to);
      if (e.kind === "realization") lines.push(to + " <|.. " + from);
      else if (e.kind === "association") lines.push(from + " --> " + to + (e.label ? " : " + escapeText(e.label) : ""));
      else lines.push(from + " ..> " + to);
    });
    return { text: lines.join("\n"), boxes: boxes };
  }

  // ---- Rendering ----

  // Mermaid's "base" theme takes its colours from the page's own, so the diagram matches either theme.
  function themeVariables() {
    var css = getComputedStyle(document.documentElement);
    function v(name) { return css.getPropertyValue(name).trim(); }
    return {
      background: v("--bg"),
      primaryColor: v("--surface"),
      mainBkg: v("--surface"),
      primaryBorderColor: v("--border"),
      nodeBorder: v("--border"),
      primaryTextColor: v("--text"),
      classText: v("--text"),
      textColor: v("--text"),
      lineColor: v("--text-muted"),
      edgeLabelBackground: v("--bg"),
      fontFamily: v("--font-ui"),
      fontSize: "13px"
    };
  }

  var drawn = null;        // { boxes, svg, width, height } of the diagram on screen
  var renderCounter = 0;
  var stale = true;        // the diagram needs redrawing next time the view is shown

  function render() {
    stale = false;
    var moduleIds = shownModules();
    var diagram = buildDiagram(moduleIds);
    var id = "vbadoc-classes-" + (++renderCounter);
    message.hidden = false;
    message.textContent = "Drawing the class diagram…";
    updateToolbar();

    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "strict",
      theme: "base",
      themeVariables: themeVariables(),
      // Big projects make big diagrams; the defaults (50,000 characters, 500 edges) are far too small.
      maxTextSize: 50000000,
      maxEdges: 100000
    });

    // Give the browser a moment to show the message before Mermaid blocks it with layout work.
    return new Promise(function (resolve) { setTimeout(resolve, 20); })
      .then(function () { return mermaid.render(id, diagram.text); })
      .then(function (result) {
        if (id !== "vbadoc-classes-" + renderCounter) return; // a newer render has started
        canvas.innerHTML = result.svg;
        var svg = canvas.querySelector("svg");
        var box = svg.viewBox.baseVal;
        svg.removeAttribute("style"); // Mermaid sets max-width, which would fight the zoom
        drawn = { boxes: diagram.boxes, svg: svg, width: box.width, height: box.height };
        tagElements(diagram.boxes);
        message.hidden = true;
        if (state.firstDraw !== false) { state.firstDraw = false; state.zoom = initialZoom(); }
        applyZoom();
        highlight();
        bringSelectionIntoView();
      })
      .catch(function (error) {
        console.error(error);
        var leftover = document.getElementById("d" + id); // Mermaid can leave its error drawing in the body
        if (leftover) leftover.remove();
        canvas.innerHTML = "";
        drawn = null;
        message.hidden = false;
        message.textContent = "The class diagram couldn't be drawn: " + (error && error.message ? error.message : error);
      });
  }

  // Marks each box with its module and each member line with its member, so clicks and highlighting can find them.
  function tagElements(boxes) {
    canvas.querySelectorAll("g.node").forEach(function (node) {
      var match = /classId-(M\d+)-/.exec(node.id);
      var moduleId = match && moduleOfClassId.get(match[1]);
      if (!moduleId) return;
      node.setAttribute("data-module", moduleId);
      node.classList.add("kind-" + stereotype(vb.modulesById.get(moduleId)));
      var box = boxes.get(moduleId);
      [["g.members-group", box.attributes], ["g.methods-group", box.methods]].forEach(function (pair) {
        var group = node.querySelector(pair[0]);
        if (!group) return;
        Array.prototype.forEach.call(group.querySelectorAll(":scope > g.label"), function (label, i) {
          if (pair[1][i]) label.setAttribute("data-member", pair[1][i]);
        });
      });
    });
  }

  // ---- Zoom ----
  //
  // The diagram is an SVG in a scrolling box: zooming changes the SVG's size, and the box scrolls as usual,
  // which keeps even very large diagrams usable.

  var ZOOM_STEP = 1.25;

  function initialZoom() {
    // Fit the width if the diagram is wider than the view, but not so small that names become unreadable.
    var fitWidth = (canvas.clientWidth - 32) / drawn.width;
    return Math.max(0.4, Math.min(1, fitWidth));
  }

  function applyZoom() {
    if (!drawn) return;
    drawn.svg.setAttribute("width", Math.round(drawn.width * state.zoom));
    drawn.svg.setAttribute("height", Math.round(drawn.height * state.zoom));
  }

  // Zooms by a factor, keeping the point under (x, y) (relative to the canvas) where it is.
  function zoomBy(factor, x, y) {
    if (!drawn) return;
    var next = Math.max(0.05, Math.min(4, state.zoom * factor));
    if (x === undefined) { x = canvas.clientWidth / 2; y = canvas.clientHeight / 2; }
    var contentX = (canvas.scrollLeft + x) / state.zoom;
    var contentY = (canvas.scrollTop + y) / state.zoom;
    state.zoom = next;
    applyZoom();
    canvas.scrollLeft = contentX * next - x;
    canvas.scrollTop = contentY * next - y;
  }

  function fit() {
    if (!drawn) return;
    state.zoom = Math.min(2, (canvas.clientWidth - 32) / drawn.width, (canvas.clientHeight - 80) / drawn.height);
    applyZoom();
    canvas.scrollLeft = 0;
    canvas.scrollTop = 0;
  }

  document.getElementById("classes-zoom-in").addEventListener("click", function () { zoomBy(ZOOM_STEP); });
  document.getElementById("classes-zoom-out").addEventListener("click", function () { zoomBy(1 / ZOOM_STEP); });
  document.getElementById("classes-fit").addEventListener("click", fit);
  // Ctrl + wheel (or a trackpad pinch) zooms around the pointer; the plain wheel scrolls.
  canvas.addEventListener("wheel", function (event) {
    if (!event.ctrlKey) return;
    event.preventDefault();
    var rect = canvas.getBoundingClientRect();
    zoomBy(Math.pow(2, -event.deltaY * 0.002), event.clientX - rect.left, event.clientY - rect.top);
  }, { passive: false });

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
    canvas.querySelectorAll("g.node[data-module]").forEach(function (node) {
      node.classList.toggle("selected", node.getAttribute("data-module") === moduleId);
    });
    canvas.querySelectorAll("[data-member]").forEach(function (label) {
      label.classList.toggle("selected-line", isSameMember(label.getAttribute("data-member"), selection));
    });
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

  // Scrolls the selected box into view if it's out of sight.
  function bringSelectionIntoView() {
    var moduleId = selectedModule();
    if (!drawn || !moduleId) return;
    var node = canvas.querySelector("g.node[data-module=\"" + CSS.escape(moduleId) + "\"]");
    if (!node) return;
    var box = node.getBoundingClientRect();
    var view = canvas.getBoundingClientRect();
    if (box.top >= view.top + 40 && box.bottom <= view.bottom && box.left >= view.left && box.right <= view.right) return;
    canvas.scrollLeft += box.left - view.left - Math.max(16, (view.width - box.width) / 2);
    canvas.scrollTop += box.top - view.top - Math.max(56, (view.height - box.height) / 2);
  }

  var clickedHere = false; // a selection made by clicking in the diagram needs no scrolling

  canvas.addEventListener("click", function (event) {
    var line = event.target.closest("[data-member]");
    var node = event.target.closest("g.node[data-module]");
    clickedHere = true;
    if (line) vb.select(line.getAttribute("data-member"));
    else if (node) vb.select(node.getAttribute("data-module"));
    else if (!event.target.closest("g.edgeLabel")) vb.select(null);
    clickedHere = false;
  });

  // ---- Toolbar ----

  function updateToolbar() {
    var moduleId = selectedModule();
    focusButton.disabled = !moduleId && !state.focus;
    focusButton.textContent = state.focus ? "Show all" : "Focus";
    focusButton.setAttribute("aria-pressed", state.focus ? "true" : "false");
    // The checkboxes show what is drawn, including dependencies left out automatically.
    filterMenu.querySelectorAll("input[data-option]").forEach(function (box) {
      var key = box.getAttribute("data-option");
      box.checked = key === "dependencies" ? showDependencies() : state.show[key];
    });
    var autoHidden = state.show.dependencies === null && !showDependencies();
    note.hidden = !autoHidden;
    note.textContent = autoHidden ? dependencyCount + " dependencies hidden to keep drawing quick: use Focus, or Show…" : "";
  }

  focusButton.addEventListener("click", function () {
    state.focus = !state.focus;
    state.focusModule = state.focus ? selectedModule() : null;
    state.firstDraw = true; // a different diagram: fit its width again
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

  function arrow(dashed, head) {
    var heads = {
      triangle: "<path d=\"M24,-5 L33,0 L24,5 Z\" fill=\"var(--bg)\" stroke=\"currentColor\"></path>",
      open: "<path d=\"M26,-4 L33,0 L26,4\" fill=\"none\" stroke=\"currentColor\"></path>"
    };
    return "<svg width=\"36\" height=\"12\" viewBox=\"0 -6 36 12\" aria-hidden=\"true\"><path d=\"M1,0 L" + (head === "triangle" ? 24 : 32) +
      ",0\" stroke=\"currentColor\"" + (dashed ? " stroke-dasharray=\"4 3\"" : "") + "></path>" + heads[head] + "</svg>";
  }

  document.getElementById("classes-legend-body").innerHTML =
    "<ul>" +
    "<li>" + arrow(true, "triangle") + "<span>Implements</span></li>" +
    "<li>" + arrow(false, "open") + "<span>Holds in a variable (labelled)</span></li>" +
    "<li>" + arrow(true, "open") + "<span>Uses</span></li>" +
    "</ul><p class=\"muted\" style=\"margin:4px 0 0\">+ public, − private, ~ friend. Click a box or a line to see its source.</p>";

  // ---- Wiring to the rest of the page ----

  function drawIfNeeded() {
    if (vb.activeView() !== "classes") return;
    if (stale || !drawn) render(); else { highlight(); bringSelectionIntoView(); }
  }

  vb.onViewChange(drawIfNeeded);

  vb.onSelectionChange(function () {
    // In focus mode the diagram follows the selection to another module.
    if (state.focus && selectedModule() && selectedModule() !== state.focusModule) {
      state.focusModule = selectedModule();
      state.firstDraw = true;
      if (vb.activeView() === "classes") render(); else stale = true;
      return;
    }
    highlight();
    if (!clickedHere) bringSelectionIntoView();
  });

  // Mermaid bakes colours into the SVG, so a theme change means drawing again.
  vb.onThemeChange(function () {
    if (vb.activeView() === "classes") render(); else stale = true;
  });

  window.vbadocClasses = {
    // For tests: the Mermaid text of the current diagram, and whether a drawing is on screen.
    text: function () { return buildDiagram(shownModules()).text; },
    drawn: function () { return !!drawn && message.hidden; },
    zoom: function () { return state.zoom; }
  };

  updateToolbar();
  drawIfNeeded();
})();
