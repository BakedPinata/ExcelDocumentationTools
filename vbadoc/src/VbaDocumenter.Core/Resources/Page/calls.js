// calls.js: the Calls tab, the procedures each procedure calls, in the order they appear in its code (step E2 of the
// feedback plan), as a tree opened one level at a time, so its size never matters.
//
// The tree starts from the entry points (macros, event handlers, worksheet functions) and the favourites, or, once a
// procedure is focused (Ctrl+click here, or a focused selection in another view), from that procedure; Entry points
// goes back. A click on a name only selects, so the opened path stays put: that's this tab's exception to Focus on
// click. "line 12" selects the procedure that makes the call and shows the line.
//
// Calls / Called by turns the tree round. A call through an interface opens to the implementations, and RaiseEvent to
// the handlers; turned round, an implementation or handler opens to what calls the interface member or raises the
// event. A procedure already open higher up the same branch is recursion (↻) and doesn't open again. Calls to items
// the views don't show (hidden, filtered out, or left out by the Show… choices) are left out and counted, and
// late-bound calls are leaves showing their expression.
(function () {
  "use strict";

  var vb = window.vbadoc;
  var data = vb.data;
  var escapeHtml = vb.escapeHtml;

  var CALLABLE = { sub: true, "function": true, propertyGet: true, propertyLet: true, propertySet: true, event: true, declare: true };
  var KIND_LABELS = {
    sub: "Sub", "function": "Function", propertyGet: "Property Get", propertyLet: "Property Let", propertySet: "Property Set",
    event: "Event", declare: "Declare"
  };
  var ENTRY_GROUPS = [
    ["macro", "Macros"],
    ["eventHandler", "Event handlers"],
    ["worksheetFunction", "Worksheet functions"]
  ];

  function loadSetting(key, fallback) { try { return localStorage.getItem(key) || fallback; } catch (e) { return fallback; } }
  function saveSetting(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* not remembered */ } }

  // ---- What calls what ----

  var callsFrom = new Map();  // procedure -> its calls, in order
  var callsTo = new Map();    // procedure -> [{ from, line }]
  data.calls.forEach(function (c) {
    if (!callsFrom.has(c.from)) callsFrom.set(c.from, []);
    callsFrom.get(c.from).push(c);
    if (!c.to) return;
    if (!callsTo.has(c.to)) callsTo.set(c.to, []);
    callsTo.get(c.to).push({ from: c.from, line: c.line });
  });

  // Interface members and their implementations, events and their WithEvents handlers.
  var implementationsOf = new Map();
  var implementedMember = new Map();
  var handlersOf = new Map();
  var handledEvent = new Map();
  function add(map, key, value) { if (!map.has(key)) map.set(key, []); map.get(key).push(value); }
  data.memberEdges.forEach(function (e) {
    var from = vb.membersById.get(e.from);
    var to = vb.membersById.get(e.to);
    if (!from || !to) return;
    if (e.kind === "implements") { add(implementationsOf, e.to, e.from); implementedMember.set(e.from, e.to); }
    if (e.kind === "events" && from.role === "withEventsHandler" && to.kind === "event") { add(handlersOf, e.to, e.from); handledEvent.set(e.from, e.to); }
  });

  function isProcedure(id) { var m = vb.membersById.get(id); return !!m && !!CALLABLE[m.kind]; }
  function nameOf(id) { var m = vb.membersById.get(id); return m ? m.module + "." + m.name : id; }
  function kindOf(id) { var m = vb.membersById.get(id); return m ? KIND_LABELS[m.kind] || m.kind : ""; }
  function shown(id) { return vb.kept(id) && vb.shows(id); }
  function byName(a, b) { var x = nameOf(a).toLowerCase(), y = nameOf(b).toLowerCase(); return x < y ? -1 : x > y ? 1 : 0; }

  // A node's children, turned the way the tree is: { rows, leftOut }. Each row is { id, line, lineIn, relation } or
  // { lateBound, line, lineIn }; lineIn is the procedure whose line it is.
  function childrenOf(id) {
    var rows = [];
    var leftOut = 0;
    function push(row) { if (row.id && !shown(row.id)) leftOut++; else rows.push(row); }
    if (direction === "calls") {
      (implementationsOf.get(id) || []).slice().sort(byName).forEach(function (impl) { push({ id: impl, relation: "implemented by" }); });
      (handlersOf.get(id) || []).slice().sort(byName).forEach(function (handler) { push({ id: handler, relation: "handled by" }); });
      (callsFrom.get(id) || []).forEach(function (c) {
        push(c.to ? { id: c.to, line: c.line, lineIn: id } : { lateBound: c.lateBound, line: c.line, lineIn: id });
      });
    } else {
      if (implementedMember.has(id)) push({ id: implementedMember.get(id), relation: "implements" });
      if (handledEvent.has(id)) push({ id: handledEvent.get(id), relation: "handles" });
      (callsTo.get(id) || []).forEach(function (c) { push({ id: c.from, line: c.line, lineIn: c.from }); });
    }
    return { rows: rows, leftOut: leftOut };
  }

  function hasChildren(id) {
    if (direction === "calls") return (callsFrom.get(id) || []).length > 0 || implementationsOf.has(id) || handlersOf.has(id);
    return (callsTo.get(id) || []).length > 0 || implementedMember.has(id) || handledEvent.has(id);
  }

  // ---- The view ----

  var view = document.getElementById("view-calls");
  var tree = document.getElementById("calls-tree");
  var message = document.getElementById("calls-message");
  var rootNote = document.getElementById("calls-root-note");
  var focusButton = document.getElementById("calls-focus");
  var direction = loadSetting("vbadoc-calls-direction", "calls") === "calledBy" ? "calledBy" : "calls";
  var root = null;            // the procedure (or module) the tree starts from, or null for the entry points
  var open = new Set();       // keys of the open rows, kept while the tree is drawn again
  var drawn = false;

  // What the tree starts from: the root's procedures, or the entry points and favourites in groups.
  function roots() {
    if (root) {
      if (vb.membersById.has(root)) return [{ id: root, key: root }];
      var procedures = data.members.filter(function (m) { return m.module === root && CALLABLE[m.kind] && m.kind !== "event" && m.kind !== "declare"; });
      return [{ group: "Procedures in " + root, key: "g:" + root, children: procedures.map(function (m) { return m.id; }) }];
    }
    var groups = [];
    var favourites = vb.favourites().filter(isProcedure);
    if (favourites.length) groups.push({ group: "Favourites", key: "g:favourites", children: favourites });
    if (direction === "calls") {
      var byRole = { macro: [], eventHandler: [], worksheetFunction: [] };
      data.members.forEach(function (m) {
        var role = m.role === "builtInEventHandler" || m.role === "controlEventHandler" ? "eventHandler" : m.role;
        if (byRole[role]) byRole[role].push(m.id);
      });
      ENTRY_GROUPS.forEach(function (g) {
        if (byRole[g[0]].length) groups.push({ group: g[1], key: "g:" + g[0], children: byRole[g[0]].sort(byName) });
      });
    }
    return groups;
  }

  function lineButton(row) {
    return row.line ? " <button type=\"button\" class=\"link-button calls-line\" data-call-line=\"" + row.line + "\" data-call-in=\"" +
      escapeHtml(row.lineIn) + "\" title=\"Show line " + row.line + " of " + escapeHtml(nameOf(row.lineIn)) + "\">line " + row.line + "</button>" : "";
  }

  function rowHtml(row, depth, key, path) {
    var li = "<li role=\"treeitem\" tabindex=\"-1\" aria-level=\"" + (depth + 1) + "\" data-key=\"" + escapeHtml(key) + "\"";
    if (row.lateBound !== undefined) {
      return li + " class=\"calls-late\"><div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\"></span>" +
        "<code class=\"tree-label\">" + escapeHtml(row.lateBound) + "</code> <span class=\"tree-detail\" title=\"Called on an Object or Variant, so what it calls can't be known without running the code\">late-bound</span>" +
        lineButton(row) + "</div></li>";
    }
    var recursive = path.indexOf(row.id) >= 0;
    var expandable = !recursive && hasChildren(row.id);
    var isOpen = expandable && open.has(key);
    var member = vb.membersById.get(row.id);
    var html = li + " data-id=\"" + escapeHtml(row.id) + "\" aria-selected=\"" + (row.id === vb.selection()) + "\"" +
      (expandable ? " aria-expanded=\"" + isOpen + "\"" : "") + (isOpen ? "" : " class=\"collapsed\"") + ">" +
      "<div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\" aria-hidden=\"true\"></span>" +
      (row.relation ? "<span class=\"calls-relation\">" + escapeHtml(row.relation) + "</span> " : "") +
      "<a href=\"#\" class=\"tree-label calls-name\" data-call-select=\"" + escapeHtml(row.id) + "\">" + escapeHtml(nameOf(row.id)) + "</a>" +
      " <span class=\"tree-detail\">" + escapeHtml(kindOf(row.id)) + "</span>" +
      (member && member.role !== "none" && depth === 0 ? " <span class=\"tree-badge\">" + escapeHtml(vb.roleLabel(member.role)) + "</span>" : "") +
      (recursive ? " <span class=\"calls-recursion\" title=\"Already open above, on this branch: recursion\">↻</span>" : "") +
      lineButton(row) + "</div>";
    if (isOpen) html += childrenHtml(row.id, depth + 1, key, path.concat([row.id]));
    return html + "</li>";
  }

  function childrenHtml(id, depth, key, path) {
    var children = childrenOf(id);
    var html = "<ul role=\"group\">" + children.rows.map(function (row, i) {
      return rowHtml(row, depth, key + "/" + i + ":" + (row.id || ""), path);
    }).join("");
    if (children.leftOut > 0) {
      html += "<li class=\"calls-left-out\" role=\"none\"><div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\"></span>" +
        "<span class=\"muted\">+ " + children.leftOut + (children.leftOut === 1 ? " call" : " calls") + " to items the views don't show (hidden, filtered or left out by Show…)</span></div></li>";
    }
    return html + "</ul>";
  }

  function draw() {
    drawn = true;
    var list = roots();
    rootNote.hidden = !root;
    if (root) rootNote.querySelector("span").textContent = (direction === "calls" ? "Calls from " : "Called by, into ") + nameOf(root);
    if (list.length === 0) {
      tree.innerHTML = "";
      message.hidden = false;
      message.textContent = direction === "calls"
        ? "No macros, event handlers, worksheet functions or favourite procedures to start from. Ctrl+click a procedure to start from it."
        : "Called by works back from one procedure: Ctrl+click one, here or in another view, or star it as a favourite.";
      return;
    }
    message.hidden = true;
    tree.innerHTML = list.map(function (item) {
      if (!item.group) return rowHtml({ id: item.id }, 0, item.key, []);
      var isOpen = !open.has(item.key + ":closed");
      return "<li role=\"treeitem\" tabindex=\"-1\" aria-level=\"1\" class=\"group" + (isOpen ? "" : " collapsed") + "\" data-key=\"" +
        escapeHtml(item.key) + "\" aria-expanded=\"" + isOpen + "\"><div class=\"tree-row\" style=\"--depth:0\"><span class=\"tree-twisty\" aria-hidden=\"true\"></span>" +
        "<span class=\"tree-label\">" + escapeHtml(item.group) + "</span><span class=\"tree-detail\">" + item.children.length + "</span></div>" +
        "<ul role=\"group\">" + item.children.map(function (id) { return rowHtml({ id: id }, 1, item.key + "/" + id, []); }).join("") + "</ul></li>";
    }).join("");
    var first = tree.querySelector("li[role=\"treeitem\"]");
    if (first) first.tabIndex = 0;
    view.querySelectorAll("[data-calls-direction]").forEach(function (b) {
      b.setAttribute("aria-checked", b.getAttribute("data-calls-direction") === direction ? "true" : "false");
    });
    focusButton.setAttribute("aria-pressed", vb.focusOnClick() ? "true" : "false");
  }

  function drawIfVisible() { if (vb.activeView() === "calls") draw(); else drawn = false; }

  // A focused procedure (or module) becomes the root, and opens one level.
  function followFocus() {
    var selection = vb.selection();
    if (!vb.focused() || !selection || (!isProcedure(selection) && !vb.modulesById.has(selection))) return false;
    if (root === selection) return false;
    root = selection;
    open = new Set([root]);
    return true;
  }

  function setOpen(li, value) {
    var key = li.getAttribute("data-key");
    if (li.classList.contains("group")) { if (value) open.delete(key + ":closed"); else open.add(key + ":closed"); }
    else if (value) open.add(key); else open.delete(key);
    var focusedKey = key;
    draw();
    var again = tree.querySelector("li[data-key=\"" + CSS.escape(focusedKey) + "\"]");
    if (again) focusRow(again);
  }

  function focusRow(li) {
    tree.querySelectorAll("li[tabindex=\"0\"]").forEach(function (other) { other.tabIndex = -1; });
    li.tabIndex = 0;
    li.focus();
  }

  // ---- Clicks and keys ----

  tree.addEventListener("click", function (event) {
    var name = event.target.closest("[data-call-select]");
    if (name) {
      event.preventDefault();
      // A plain click only selects; Ctrl+click focuses, which roots the tree there.
      var focus = event.ctrlKey || event.metaKey;
      vb.select(name.getAttribute("data-call-select"), focus ? !vb.focusOnClick() : vb.focusOnClick());
      return;
    }
    var line = event.target.closest("[data-call-line]");
    if (line) {
      event.preventDefault();
      vb.select(line.getAttribute("data-call-in"), vb.focusOnClick());
      vb.showLine(parseInt(line.getAttribute("data-call-line"), 10));
      return;
    }
    var li = event.target.closest("li[aria-expanded]");
    if (li && tree.contains(li)) setOpen(li, li.getAttribute("aria-expanded") !== "true");
  });

  function visibleRows() {
    return Array.prototype.filter.call(tree.querySelectorAll("li[role=\"treeitem\"]"), function (li) {
      for (var at = li.parentElement.closest("li"); at; at = at.parentElement.closest("li")) if (at.getAttribute("aria-expanded") !== "true") return false;
      return true;
    });
  }

  tree.addEventListener("keydown", function (event) {
    var li = event.target.closest("li[role=\"treeitem\"]");
    if (!li) return;
    var rows = visibleRows();
    var at = rows.indexOf(li);
    var handled = true;
    if (event.key === "ArrowDown") { if (rows[at + 1]) focusRow(rows[at + 1]); }
    else if (event.key === "ArrowUp") { if (at > 0) focusRow(rows[at - 1]); }
    else if (event.key === "Home") { focusRow(rows[0]); }
    else if (event.key === "End") { focusRow(rows[rows.length - 1]); }
    else if (event.key === "ArrowRight") {
      if (li.getAttribute("aria-expanded") === "false") setOpen(li, true);
      else if (li.getAttribute("aria-expanded") === "true" && rows[at + 1]) focusRow(rows[at + 1]);
    } else if (event.key === "ArrowLeft") {
      if (li.getAttribute("aria-expanded") === "true") setOpen(li, false);
      else { var parent = li.parentElement.closest("li"); if (parent) focusRow(parent); }
    } else if (event.key === "Enter" && li.dataset.id) {
      var focus = event.ctrlKey || event.metaKey;
      vb.select(li.dataset.id, focus ? !vb.focusOnClick() : vb.focusOnClick());
    } else handled = false;
    if (handled) { event.preventDefault(); event.stopPropagation(); }
  });

  view.querySelectorAll("[data-calls-direction]").forEach(function (button) {
    button.addEventListener("click", function () {
      if (direction === button.getAttribute("data-calls-direction")) return;
      direction = button.getAttribute("data-calls-direction");
      saveSetting("vbadoc-calls-direction", direction);
      open = new Set(root ? [root] : []);
      draw();
    });
  });

  document.getElementById("calls-entry-points").addEventListener("click", function () {
    root = null;
    open = new Set();
    draw();
  });

  document.getElementById("calls-collapse").addEventListener("click", function () {
    open = new Set();
    draw();
  });

  focusButton.addEventListener("click", function () { vb.setFocusOnClick(!vb.focusOnClick()); });

  // ---- Wiring to the rest of the page ----

  vb.onViewChange(function () { if (vb.activeView() === "calls" && !drawn) draw(); });
  vb.onSelectionChange(function () {
    var rerooted = followFocus();
    if (vb.selection() === null && root !== null) { root = null; open = new Set(); rerooted = true; }
    if (rerooted) { drawIfVisible(); return; }
    tree.querySelectorAll("li[data-id]").forEach(function (li) { li.setAttribute("aria-selected", li.dataset.id === vb.selection() ? "true" : "false"); });
  });
  vb.onFocusChange(function () { followFocus(); drawIfVisible(); });
  vb.onShownChange(drawIfVisible);
  vb.onChoicesChange(drawIfVisible);
  vb.onFavouritesChange(drawIfVisible);

  followFocus();
  if (vb.activeView() === "calls") draw();

  // For tests: the tree's root and direction.
  window.vbadocCalls = {
    root: function () { return root; },
    direction: function () { return direction; }
  };
})();
