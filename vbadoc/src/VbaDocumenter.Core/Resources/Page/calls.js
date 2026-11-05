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
//
// The logic around each call (step E3) shows as rows: an If, ElseIf or Else branch, a Select Case and each Case, a
// loop, a With or an On Error handler holds the calls made inside it, in order, open to begin with. A block with no
// calls in it isn't shown, and its text is cut to about 60 characters. Turned round, a caller says which block the
// call is in.
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
  var callsTo = new Map();    // procedure -> [{ from, line, block }]
  data.calls.forEach(function (c) {
    if (!callsFrom.has(c.from)) callsFrom.set(c.from, []);
    callsFrom.get(c.from).push(c);
    if (!c.to) return;
    if (!callsTo.has(c.to)) callsTo.set(c.to, []);
    callsTo.get(c.to).push({ from: c.from, line: c.line, block: c.block });
  });

  var blocksOf = new Map();   // procedure -> its blocks, by id
  data.blocks.forEach(function (b) {
    if (!blocksOf.has(b.from)) blocksOf.set(b.from, []);
    blocksOf.get(b.from)[b.id] = b;
  });

  var BLOCK_TEXT_LENGTH = 60;
  function cut(text, length) { return text.length > length ? text.slice(0, length - 1) + "…" : text; }

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

  // A node's children, turned the way the tree is: { rows, leftOut }. Each row is { id, line, lineIn, relation, within },
  // { lateBound, line, lineIn } or a block, { block, line, lineIn, rows }; lineIn is the procedure whose line it is.
  function childrenOf(id) {
    var rows = [];
    var leftOut = 0;
    function push(row) { if (row.id && !shown(row.id)) leftOut++; else rows.push(row); }
    if (direction === "calls") {
      (implementationsOf.get(id) || []).slice().sort(byName).forEach(function (impl) { push({ id: impl, relation: "implemented by" }); });
      (handlersOf.get(id) || []).slice().sort(byName).forEach(function (handler) { push({ id: handler, relation: "handled by" }); });
      // Each call goes in its block's row, made when its first call comes, so blocks are in the order of the code and a
      // block with no calls shown isn't there at all.
      var blocks = blocksOf.get(id) || [];
      var blockRows = new Map();
      function rowsOf(blockId) {
        if (blockId === null || blockId === undefined || !blocks[blockId]) return rows;
        if (blockRows.has(blockId)) return blockRows.get(blockId).rows;
        var block = blocks[blockId];
        var row = { block: block, line: block.line, lineIn: id, rows: [] };
        blockRows.set(blockId, row);
        rowsOf(block.parent).push(row);
        return row.rows;
      }
      (callsFrom.get(id) || []).forEach(function (c) {
        if (c.to && !shown(c.to)) { leftOut++; return; }
        rowsOf(c.block).push(c.to ? { id: c.to, line: c.line, lineIn: id } : { lateBound: c.lateBound, line: c.line, lineIn: id });
      });
    } else {
      if (implementedMember.has(id)) push({ id: implementedMember.get(id), relation: "implements" });
      if (handledEvent.has(id)) push({ id: handledEvent.get(id), relation: "handles" });
      (callsTo.get(id) || []).forEach(function (c) {
        var block = c.block !== null && c.block !== undefined && blocksOf.has(c.from) ? blocksOf.get(c.from)[c.block] : null;
        push({ id: c.from, line: c.line, lineIn: c.from, within: block ? block.text : null });
      });
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
  var pathsByKey = new Map(); // a drawn row's key -> { id, path }: what it is and the procedures above it

  // ---- Open all below ----
  //
  // Opens a procedure's calls and everything below them, as far as recursion allows, so a whole entry point can be
  // read or drawn at once. A large project could hold a great many, so it stops after OPEN_ALL_LIMIT and says so.

  var OPEN_ALL_LIMIT = 2000;
  var openNote = document.getElementById("calls-open-note");

  function openAll(id, key, path, budget) {
    if (budget.left <= 0) { budget.stopped = true; return; }
    open.add(key);
    budget.left--;
    var below = path.concat([id]);
    (function each(rows, parentKey) {
      rows.forEach(function (row, i) {
        var rowKey = parentKey + (row.block ? "/b" + row.block.id : "/" + i + ":" + (row.id || ""));
        if (row.block) { open.delete(rowKey + ":closed"); each(row.rows, rowKey); return; }
        if (!row.id || below.indexOf(row.id) >= 0 || !hasChildren(row.id)) return;
        openAll(row.id, rowKey, below, budget);
      });
    })(childrenOf(id).rows, key);
  }

  function openAllBelow(starts) {
    var budget = { left: OPEN_ALL_LIMIT, stopped: false };
    starts.forEach(function (s) { openAll(s.id, s.key, s.path, budget); });
    openNote.hidden = !budget.stopped;
    openNote.textContent = budget.stopped ? "Opened the first " + OPEN_ALL_LIMIT.toLocaleString() + " calls; open the rest one at a time." : "";
    draw();
  }

  // A group's procedures, each opened all the way down.
  function openGroup(key) {
    var group = roots().filter(function (item) { return item.key === key; })[0];
    if (!group) return;
    open.delete(key + ":closed");
    openAllBelow(group.children.filter(hasChildren).map(function (id) { return { id: id, key: key + "/" + id, path: [] }; }));
  }

  var OPEN_ALL_BUTTON = "<button type=\"button\" class=\"calls-open-all\" tabindex=\"-1\" title=\"Open everything below (or press *)\" aria-label=\"Open everything below\">" +
    "<svg class=\"icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M4.5 3.5 8 7l3.5-3.5M4.5 9 8 12.5 11.5 9\"/></svg>" + "</button>";

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
    if (row.block) {
      // Open to begin with, as the calls it holds are what it's there for.
      var blockOpen = !open.has(key + ":closed");
      return li + " class=\"calls-block" + (blockOpen ? "" : " collapsed") + "\" data-open-first aria-expanded=\"" + blockOpen + "\">" +
        "<div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\" aria-hidden=\"true\"></span>" +
        "<code class=\"tree-label calls-block-text\" title=\"" + escapeHtml(row.block.text) + "\">" + escapeHtml(cut(row.block.text, BLOCK_TEXT_LENGTH)) + "</code>" +
        lineButton(row) + "</div>" + rowsHtml(row.rows, depth + 1, key, path) + "</li>";
    }
    if (row.lateBound !== undefined) {
      return li + " class=\"calls-late\"><div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\"></span>" +
        "<code class=\"tree-label\">" + escapeHtml(row.lateBound) + "</code> <span class=\"tree-detail\" title=\"Called on an Object or Variant, so what it calls can't be known without running the code\">late-bound</span>" +
        lineButton(row) + "</div></li>";
    }
    var recursive = path.indexOf(row.id) >= 0;
    var expandable = !recursive && hasChildren(row.id);
    var isOpen = expandable && open.has(key);
    var member = vb.membersById.get(row.id);
    pathsByKey.set(key, { id: row.id, path: path });
    var html = li + " data-id=\"" + escapeHtml(row.id) + "\" aria-selected=\"" + (row.id === vb.selection()) + "\"" +
      (expandable ? " aria-expanded=\"" + isOpen + "\"" : "") + (isOpen ? "" : " class=\"collapsed\"") + ">" +
      "<div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\" aria-hidden=\"true\"></span>" +
      (row.relation ? "<span class=\"calls-relation\">" + escapeHtml(row.relation) + "</span> " : "") +
      "<a href=\"#\" class=\"tree-label calls-name\" data-call-select=\"" + escapeHtml(row.id) + "\">" + escapeHtml(nameOf(row.id)) + "</a>" +
      " <span class=\"tree-detail\">" + escapeHtml(kindOf(row.id)) + "</span>" +
      (member && member.role !== "none" && depth === 0 ? " <span class=\"tree-badge\">" + escapeHtml(vb.roleLabel(member.role)) + "</span>" : "") +
      (recursive ? " <span class=\"calls-recursion\" title=\"Already open above, on this branch: recursion\">↻</span>" : "") +
      lineButton(row) +
      (row.within ? " <span class=\"calls-within muted\" title=\"" + escapeHtml("The call is inside " + row.within) + "\">in " +
        escapeHtml(cut(row.within, 40)) + "</span>" : "") +
      (expandable ? OPEN_ALL_BUTTON.replace("<button ", "<button data-open-all=\"" + escapeHtml(key) + "\" ") : "") + "</div>";
    if (isOpen) html += childrenHtml(row.id, depth + 1, key, path.concat([row.id]));
    return html + "</li>";
  }

  function rowsHtml(rows, depth, key, path) {
    return "<ul role=\"group\">" + rows.map(function (row, i) {
      return rowHtml(row, depth, key + (row.block ? "/b" + row.block.id : "/" + i + ":" + (row.id || "")), path);
    }).join("") + "</ul>";
  }

  function childrenHtml(id, depth, key, path) {
    var children = childrenOf(id);
    var html = rowsHtml(children.rows, depth, key, path).slice(0, -"</ul>".length);
    if (children.leftOut > 0) {
      html += "<li class=\"calls-left-out\" role=\"none\"><div class=\"tree-row\" style=\"--depth:" + depth + "\"><span class=\"tree-twisty\"></span>" +
        "<span class=\"muted\">+ " + children.leftOut + (children.leftOut === 1 ? " call" : " calls") + " to items the views don't show (hidden, filtered or left out by Show…)</span></div></li>";
    }
    return html + "</ul>";
  }

  function draw() {
    drawn = true;
    pathsByKey = new Map();
    var list = roots();
    var sequence = showing === "sequence" && direction === "calls"; // Called by has no diagram
    view.querySelectorAll("[data-calls-show]").forEach(function (b) {
      b.setAttribute("aria-checked", (b.getAttribute("data-calls-show") === "sequence") === sequence ? "true" : "false");
      if (b.getAttribute("data-calls-show") === "sequence") {
        b.disabled = direction !== "calls";
        b.title = direction === "calls" ? "Draw what's open as a sequence diagram" : "The sequence diagram follows Calls, not Called by";
      }
    });
    tree.hidden = sequence;
    sequenceBox.hidden = !sequence;
    rootNote.hidden = !root;
    if (root) rootNote.querySelector("span").textContent = (direction === "calls" ? "Calls from " : "Called by, into ") + nameOf(root);
    if (list.length === 0) {
      tree.innerHTML = "";
      sequenceBox.innerHTML = "";
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
        "<span class=\"tree-label\">" + escapeHtml(item.group) + "</span><span class=\"tree-detail\">" + item.children.length + "</span>" +
        OPEN_ALL_BUTTON.replace("<button ", "<button data-open-all-group=\"" + escapeHtml(item.key) + "\" ") + "</div>" +
        "<ul role=\"group\">" + item.children.map(function (id) { return rowHtml({ id: id }, 1, item.key + "/" + id, []); }).join("") + "</ul></li>";
    }).join("");
    var first = tree.querySelector("li[role=\"treeitem\"]");
    if (first) first.tabIndex = 0;
    if (sequence) drawSequence(list);
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
    if (li.classList.contains("group") || li.hasAttribute("data-open-first")) { if (value) open.delete(key + ":closed"); else open.add(key + ":closed"); }
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


  // ---- Sequence diagram (step E5) ----
  //
  // What's open in the tree, drawn as a sequence diagram for each open starting procedure: modules as lifelines, in the
  // order they first take part; calls as messages in the order of the code, dashed to an implementation or handler;
  // late-bound calls to a lifeline of their own; and the blocks around calls as frames. The tree and the diagram share
  // what's open: a message's ▸ opens its calls, a frame's label closes or opens it, and either narrows both. Drawn with
  // d3 and set out by the page, so text is only ever text.

  var showing = loadSetting("vbadoc-calls-show", "tree") === "sequence" ? "sequence" : "tree";
  var sequenceBox = document.getElementById("calls-sequence");

  var MIN_LANE = 170, MAX_LANE = 380, LANE = MIN_LANE, CODE_CHAR = 6.8, HEAD = 30, ROW = 30, FRAME_HEAD = 26, MARGIN = 16, TITLE = 26, CHAR = 6.4;
  var FRAME_WORDS = { "if": "if", elseIf: "else if", "else": "else", select: "select", "case": "case", "for": "loop", "do": "loop", "while": "loop", "with": "with", onError: "on error" };

  function moduleOf(id) { var m = vb.membersById.get(id); return m ? m.module : id; }
  function memberName(id) { var m = vb.membersById.get(id); return m ? m.name : id; }

  // The open starting procedures, as the tree has them.
  function sequenceStarts(list) {
    var starts = [];
    list.forEach(function (item) {
      if (!item.group) { if (open.has(item.key) && hasChildren(item.id)) starts.push({ id: item.id, key: item.key }); return; }
      if (open.has(item.key + ":closed")) return;
      item.children.forEach(function (id) {
        var key = item.key + "/" + id;
        if (open.has(key) && hasChildren(id)) starts.push({ id: id, key: key });
      });
    });
    return starts;
  }

  // The lifelines and, in order, the messages, frames and activations below a starting procedure, following the tree's
  // rows and keys.
  function sequenceModel(start) {
    var lanes = [];
    var laneOf = new Map();
    function lane(name, late) {
      if (!laneOf.has(name)) { laneOf.set(name, lanes.length); lanes.push({ name: name, late: !!late }); }
      return laneOf.get(name);
    }
    var items = [];
    lane(moduleOf(start.id));
    function rowsOf(rows, callerId, key, path) {
      var from = lane(moduleOf(callerId));
      rows.forEach(function (row, i) {
        var rowKey = key + (row.block ? "/b" + row.block.id : "/" + i + ":" + (row.id || ""));
        if (row.block) {
          var blockOpen = !open.has(rowKey + ":closed");
          items.push({ type: "frameStart", block: row.block, key: rowKey, open: blockOpen, lane: from, line: row.line, lineIn: callerId });
          if (blockOpen) rowsOf(row.rows, callerId, rowKey, path);
          items.push({ type: "frameEnd" });
          return;
        }
        if (row.lateBound !== undefined) {
          items.push({ type: "message", from: from, to: lane("late-bound", true), label: row.lateBound, late: true, line: row.line, lineIn: callerId });
          return;
        }
        var recursive = path.indexOf(row.id) >= 0;
        seqPaths.set(rowKey, { id: row.id, path: path });
        var expandable = !recursive && hasChildren(row.id);
        var isOpen = expandable && open.has(rowKey);
        var to = lane(moduleOf(row.id));
        items.push({
          type: "message", from: from, to: to, label: memberName(row.id), id: row.id, relation: row.relation, recursive: recursive,
          expandable: expandable, open: isOpen, key: rowKey, line: row.line, lineIn: row.lineIn
        });
        if (isOpen) {
          items.push({ type: "activationStart", lane: to });
          rowsOf(childrenOf(row.id).rows, row.id, rowKey, path.concat([row.id]));
          items.push({ type: "activationEnd" });
        }
      });
    }
    items.push({ type: "activationStart", lane: 0 });
    rowsOf(childrenOf(start.id).rows, start.id, start.key, [start.id]);
    items.push({ type: "activationEnd" });
    return { start: start, lanes: lanes, items: items, lane: laneWidth(lanes, items) };
  }

  var seqPaths = new Map(); // a message's key -> { id, path }, for Shift+click to open all below it

  // Lanes as wide as the widest label a message between neighbours, or a lifeline's name, needs, within bounds.
  function laneWidth(lanes, items) {
    var width = MIN_LANE;
    lanes.forEach(function (l) { width = Math.max(width, l.name.length * CHAR + 32); });
    items.forEach(function (item) {
      if (item.type !== "message") return;
      var text = ((item.relation ? item.relation + ": " : "") + item.label + (item.recursive ? " ↻" : "")).length + (item.expandable ? 2 : 0);
      var span = Math.abs(item.to - item.from);
      width = Math.max(width, span === 0 ? text * CHAR + 56 : (text * CHAR + 28) / span);
    });
    return Math.min(MAX_LANE, Math.ceil(width));
  }

  // Where everything goes, from the top of this diagram down.
  function sequenceLayout(model) {
    var y = TITLE + HEAD + 14;
    var messages = [], frames = [], activations = [];
    var openFrames = [], openActivations = [];
    function touch(laneIndex) {
      openFrames.forEach(function (f) { f.min = Math.min(f.min, laneIndex); f.max = Math.max(f.max, laneIndex); });
    }
    model.items.forEach(function (item) {
      if (item.type === "frameStart") {
        var frame = { item: item, y: y, min: item.lane, max: item.lane, depth: openFrames.length };
        openFrames.push(frame);
        frames.push(frame);
        y += FRAME_HEAD;
      } else if (item.type === "frameEnd") {
        var ended = openFrames.pop();
        ended.end = y + (ended.item.open ? 10 : 2);
        y = ended.end + 6;
      } else if (item.type === "message") {
        y += ROW;
        touch(item.from);
        touch(item.to);
        var nested = openActivations.filter(function (a) { return a.lane === item.from; }).length;
        messages.push({ item: item, y: y, fromOffset: Math.max(0, nested - 1) * 5 });
      } else if (item.type === "activationStart") {
        var depth = openActivations.filter(function (a) { return a.lane === item.lane; }).length;
        openActivations.push({ lane: item.lane, y: y + (openActivations.length ? 0 : -6), depth: depth });
      } else {
        var activation = openActivations.pop();
        activation.end = y + 8;
        activations.push(activation);
        y += 4;
      }
    });
    // Outer frames are wider, so each sits inside the one around it and frames side by side line up.
    var deepest = d3.max(frames, function (f) { return f.depth; }) || 0;
    frames.forEach(function (f) { f.pad = 24 + 12 * (deepest - f.depth); });
    return { messages: messages, frames: frames, activations: activations, height: y + 20 };
  }

  function laneX(index) { return MARGIN + LANE / 2 + index * LANE; }
  function fit(text, width) { var most = Math.max(4, Math.floor(width / CHAR)); return text.length > most ? text.slice(0, most - 1) + "…" : text; }

  function drawSequence(list) {
    seqPaths = new Map();
    var starts = sequenceStarts(list);
    var box = d3.select(sequenceBox);
    box.selectAll("*").remove();
    if (starts.length === 0) {
      // Nothing open yet: a way to start, from the same starting points as the tree.
      var empty = box.append("div").attr("class", "calls-seq-empty");
      empty.append("p").attr("class", "muted").text("Open a procedure to draw its calls. Here, or in the tree:");
      var startable = [];
      list.forEach(function (item) {
        if (!item.group) startable.push({ id: item.id, key: item.key });
        else item.children.forEach(function (id) { startable.push({ id: id, key: item.key + "/" + id }); });
      });
      empty.append("div").attr("class", "calls-seq-starts").selectAll("button")
        .data(startable.filter(function (s) { return hasChildren(s.id); }).slice(0, 40)).join("button")
        .attr("type", "button").attr("class", "button").attr("data-seq-start", function (s) { return s.key; })
        .attr("data-seq-id", function (s) { return s.id; }).attr("title", "Draw its calls; Shift+click to open everything below it")
        .text(function (s) { return nameOf(s.id); });
      return;
    }
    var models = starts.map(sequenceModel);
    var layouts = models.map(sequenceLayout);
    var height = d3.sum(layouts, function (l) { return l.height; });
    var svg = box.append("svg").attr("class", "calls-seq").attr("height", height)
      .attr("role", "img").attr("aria-label", "Sequence diagram of the open calls");
    var defs = svg.append("defs");
    [["seq-arrow", "M0,-4L9,0L0,4Z"], ["seq-arrow-open", "M0,-4L9,0L0,4"]].forEach(function (m) {
      defs.append("marker").attr("id", m[0]).attr("viewBox", "0 -5 10 10").attr("refX", 9).attr("refY", 0)
        .attr("markerWidth", 8).attr("markerHeight", 8).attr("markerUnits", "userSpaceOnUse").attr("orient", "auto")
        .append("path").attr("d", m[1]);
    });
    var top = 0;
    var width = 0;
    models.forEach(function (model, n) {
      width = Math.max(width, drawOneSequence(svg.append("g").attr("class", "seq-diagram").attr("data-start", model.start.id)
        .attr("transform", "translate(0," + top + ")"), model, layouts[n]));
      top += layouts[n].height;
    });
    svg.attr("width", width + MARGIN);
  }

  // Draws one diagram and returns how wide it is.
  function drawOneSequence(g, model, layout) {
    LANE = model.lane;
    var bottom = layout.height - 10;
    g.append("text").attr("class", "seq-title").attr("x", MARGIN).attr("y", 16).text("Calls from " + nameOf(model.start.id));

    // Frames' outlines first, so lifelines and messages draw over them; their labels last, over everything.
    var frameLayer = g.append("g").attr("class", "seq-frames");

    // Lifelines.
    var lanes = g.selectAll("g.seq-lane").data(model.lanes).join("g")
      .attr("class", function (l) { return "seq-lane" + (l.late ? " late" : ""); })
      .attr("data-lane", function (l) { return l.name; });
    lanes.append("line").attr("x1", function (l, i) { return laneX(i); }).attr("x2", function (l, i) { return laneX(i); })
      .attr("y1", TITLE + HEAD).attr("y2", bottom);
    var head = lanes.append("g").attr("class", "seq-lane-head").attr("data-seq-module", function (l) { return l.late ? null : l.name; });
    head.append("rect").attr("x", function (l, i) { return laneX(i) - LANE / 2 + 8; }).attr("y", TITLE)
      .attr("width", LANE - 16).attr("height", HEAD).attr("rx", 3);
    head.append("text").attr("x", function (l, i) { return laneX(i); }).attr("y", TITLE + HEAD / 2 + 4).attr("text-anchor", "middle")
      .text(function (l) { return fit(l.name, LANE - 24); });
    head.append("title").text(function (l) {
      return l.late ? "Late-bound calls: on an Object or Variant, so what they call can't be known without running the code" : l.name;
    });

    // Activations: how long each opened procedure runs.
    layout.activations.forEach(function (a) {
      g.append("rect").attr("class", "seq-activation").attr("x", laneX(a.lane) - 4 + a.depth * 5).attr("y", a.y)
        .attr("width", 8).attr("height", Math.max(6, a.end - a.y));
    });

    // Messages.
    layout.messages.forEach(function (m) {
      var item = m.item;
      var x1 = laneX(item.from) + 4 + m.fromOffset, x2 = laneX(item.to) - 4;
      var message = g.append("g").attr("class", "seq-message" + (item.relation ? " relation" : "") + (item.late ? " late" : "") +
        (item.id && item.id === vb.selection() ? " selected" : "")).attr("data-id", item.id || null).attr("data-key", item.key || null);
      var label = message.append("text").attr("class", "seq-label");
      if (item.from === item.to) {
        var x = laneX(item.from) + 4;
        message.append("path").attr("d", "M" + x + "," + (m.y - 10) + "h28v12h-24").attr("marker-end", "url(#seq-arrow)");
        label.attr("x", x + 34).attr("y", m.y - 2);
      } else {
        var leftToRight = x2 > x1;
        message.append("line").attr("x1", leftToRight ? x1 : laneX(item.from) - 4).attr("x2", leftToRight ? x2 : laneX(item.to) + 4)
          .attr("y1", m.y).attr("y2", m.y).attr("marker-end", item.relation ? "url(#seq-arrow-open)" : "url(#seq-arrow)");
        label.attr("x", (x1 + x2) / 2).attr("y", m.y - 6).attr("text-anchor", "middle");
      }
      var room = item.from === item.to ? LANE - 40 : Math.abs(x2 - x1) - 10;
      if (item.expandable) {
        label.append("tspan").attr("class", "seq-toggle").attr("data-seq-toggle", item.key).text(item.open ? "▾ " : "▸ ")
          .append("title").text((item.open ? "Close" : "Open") + " its calls; Shift+click to open everything below it");
      }
      label.append("tspan").attr("class", "seq-name").attr("data-seq-select", item.id || null)
        .text(fit((item.relation ? item.relation + ": " : "") + item.label, room) + (item.recursive ? " ↻" : ""));
      message.append("title").text((item.late ? "Late-bound: " + item.label : nameOf(item.id) + (item.recursive ? " (recursion: already open above)" : "")) +
        (item.line ? ", line " + item.line + " of " + nameOf(item.lineIn) : ""));
    });

    // Each frame is wide enough for its label and its whole guard, and holds the frames inside it.
    layout.frames.forEach(function (f) {
      f.word = FRAME_WORDS[f.item.block.kind] || f.item.block.kind;
      f.tab = (f.word.length + 2) * CHAR + 16; // with its ▾ or ▸
      f.guard = f.item.block.kind === "else" ? "" : "[" + cut(f.item.block.text, 80) + "]";
      f.left = laneX(f.min) - f.pad;
      f.right = Math.max(laneX(f.max) + f.pad + 30, f.left + f.tab + 8 + f.guard.length * CODE_CHAR + 12);
    });
    for (var i = layout.frames.length - 1; i >= 0; i--) {
      for (var j = i - 1; j >= 0; j--) {
        var outer = layout.frames[j], inner = layout.frames[i];
        if (outer.depth === inner.depth - 1 && outer.y < inner.y && outer.end >= inner.end) { outer.right = Math.max(outer.right, inner.right + 12); break; }
      }
    }
    var widest = MARGIN + model.lanes.length * LANE + 60;

    layout.frames.forEach(function (f) {
      var left = f.left, right = f.right;
      widest = Math.max(widest, right);
      var frame = frameLayer.append("g").attr("class", "seq-frame kind-" + f.item.block.kind + (f.item.open ? "" : " closed"))
        .attr("data-key", f.item.key);
      frame.append("rect").attr("x", left).attr("y", f.y).attr("width", right - left).attr("height", f.end - f.y).attr("rx", 3);
      var word = f.word;
      var label = g.append("g").attr("class", "seq-frame-label").attr("data-seq-frame", f.item.key);
      var tabWidth = f.tab;
      label.append("path").attr("d", "M" + left + "," + f.y + "h" + tabWidth + "v12l-6,6h-" + (tabWidth - 6) + "z");
      label.append("text").attr("x", left + 5).attr("y", f.y + 13).text((f.item.open ? "▾ " : "▸ ") + word);
      label.append("title").text((f.item.open ? "Close: " : "Open: ") + f.item.block.text + " (line " + f.item.line + ")");
      if (f.guard) label.append("text").attr("class", "seq-guard").attr("x", left + tabWidth + 8).attr("y", f.y + 13).text(f.guard);
    });
    return widest;
  }

  sequenceBox.addEventListener("click", function (event) {
    var target = event.target;
    var start = target.closest("[data-seq-start]");
    if (start) {
      if (event.shiftKey) openAllBelow([{ id: start.getAttribute("data-seq-id"), key: start.getAttribute("data-seq-start"), path: [] }]);
      else { open.add(start.getAttribute("data-seq-start")); draw(); }
      return;
    }
    var toggle = target.closest("[data-seq-toggle]");
    if (toggle && event.shiftKey && seqPaths.has(toggle.getAttribute("data-seq-toggle"))) {
      var at = seqPaths.get(toggle.getAttribute("data-seq-toggle"));
      openAllBelow([{ id: at.id, key: toggle.getAttribute("data-seq-toggle"), path: at.path }]);
      return;
    }
    if (toggle) {
      var key = toggle.getAttribute("data-seq-toggle");
      if (open.has(key)) open.delete(key); else open.add(key);
      draw();
      return;
    }
    var frame = target.closest("[data-seq-frame]");
    if (frame) {
      var frameKey = frame.getAttribute("data-seq-frame") + ":closed";
      if (open.has(frameKey)) open.delete(frameKey); else open.add(frameKey);
      draw();
      return;
    }
    // A plain click only selects, as in the tree; Ctrl+click starts from it.
    var focus = event.ctrlKey || event.metaKey;
    var name = target.closest("[data-seq-select]");
    if (name) { vb.select(name.getAttribute("data-seq-select"), focus ? !vb.focusOnClick() : vb.focusOnClick()); return; }
    var module = target.closest("[data-seq-module]");
    if (module) vb.select(module.getAttribute("data-seq-module"), focus ? !vb.focusOnClick() : vb.focusOnClick());
  });

  view.querySelectorAll("[data-calls-show]").forEach(function (button) {
    button.addEventListener("click", function () {
      if (showing === button.getAttribute("data-calls-show")) return;
      showing = button.getAttribute("data-calls-show");
      saveSetting("vbadoc-calls-show", showing);
      draw();
    });
  });

  // ---- Clicks and keys ----

  tree.addEventListener("click", function (event) {
    var all = event.target.closest("[data-open-all]");
    if (all) {
      var at = pathsByKey.get(all.getAttribute("data-open-all"));
      if (at) openAllBelow([{ id: at.id, key: all.getAttribute("data-open-all"), path: at.path }]);
      return;
    }
    var group = event.target.closest("[data-open-all-group]");
    if (group) { openGroup(group.getAttribute("data-open-all-group")); return; }
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
    } else if (event.key === "*") {
      var button = li.querySelector(":scope > .tree-row .calls-open-all");
      if (button) button.click();
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
    openNote.hidden = true;
    draw();
  });

  focusButton.addEventListener("click", function () { vb.setFocusOnClick(!vb.focusOnClick()); });

  // ---- Saved views ----
  //
  // A saved view keeps where the tree starts, which rows are open, Calls or Called by, and the tree or the diagram. A
  // row is known by its path from the top, so after the workbook changes a row may not open where it did.

  function idsInKey(key) {
    var ids = [];
    key.split("/").forEach(function (part, i) {
      if (i === 0) { if (part.indexOf("g:") !== 0) ids.push(part); return; }
      var call = /^\d+:(.+)$/.exec(part);
      if (call) ids.push(call[1].replace(/:closed$/, ""));
      else if (!/^b\d+(:closed)?$/.test(part) && !/^\d+:$/.test(part)) ids.push(part.replace(/:closed$/, ""));
    });
    return ids;
  }

  vb.viewPart("calls", {
    save: function () {
      var value = { direction: direction, show: showing, open: Array.from(open).sort() };
      if (root) value.root = root;
      return value;
    },
    apply: function (value) {
      value = value && typeof value === "object" ? value : {};
      direction = value.direction === "calledBy" ? "calledBy" : "calls";
      showing = value.show === "sequence" ? "sequence" : "tree";
      saveSetting("vbadoc-calls-direction", direction);
      saveSetting("vbadoc-calls-show", showing);
      root = typeof value.root === "string" && (isProcedure(value.root) || vb.modulesById.has(value.root)) ? value.root : null;
      open = new Set(Array.isArray(value.open) ? value.open.filter(function (key) { return typeof key === "string"; }) : []);
      openNote.hidden = true;
      drawn = false;
    },
    ids: function (value) {
      var ids = value && typeof value.root === "string" ? [value.root] : [];
      (value && Array.isArray(value.open) ? value.open : []).forEach(function (key) {
        if (typeof key === "string") ids = ids.concat(idsInKey(key));
      });
      return ids;
    }
  });

  // ---- Wiring to the rest of the page ----

  vb.onViewChange(function () { if (vb.activeView() === "calls" && !drawn) draw(); });
  vb.onSelectionChange(function () {
    var rerooted = followFocus();
    if (vb.selection() === null && root !== null) { root = null; open = new Set(); rerooted = true; }
    if (rerooted) { drawIfVisible(); return; }
    tree.querySelectorAll("li[data-id]").forEach(function (li) { li.setAttribute("aria-selected", li.dataset.id === vb.selection() ? "true" : "false"); });
    sequenceBox.querySelectorAll("g.seq-message[data-id]").forEach(function (m) {
      m.classList.toggle("selected", m.getAttribute("data-id") === vb.selection());
    });
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
    direction: function () { return direction; },
    showing: function () { return showing; }
  };
})();
