// shell.js: the page shell both tools' pages are built on (see shared/README.md). It owns the frame around the views:
// remembered settings, the theme, the tabs, the side panel and its splitter, Load folder and the source text. A tool's
// page.js creates it, fills the side panel, and builds the tool's own window.vbadoc or window.pqdoc on top, which is
// what the tool's views use.
//
//   var shell = ExcelDocumenterShell.create(options)
//
// options:
//   tool           "vbadoc" or "pqdoc". The data block is #<tool>-data, embedded source is in pre.<tool>-source, and
//                  every setting is stored as <tool>-<name>.
//   panel          the side panel's name in stored settings: <tool>-<panel>-size and <tool>-<panel>-hidden
//   rememberView   whether the open tab is remembered, as <tool>-view
//
// The shell gives the tool:
//   shell.data                         the page's data
//   shell.escapeHtml(text)             text made safe for HTML content and double-quoted attributes
//   shell.settings.load(name) / save(name, value) / remove(name)   remembered per browser; never throws
//   shell.theme() / onThemeChange(fn)  "light" or "dark"
//   shell.activeView() / showView(name, focus) / onViewChange(fn)   tab #tab-<name> shows view #view-<name>
//   shell.start()                      shows the remembered tab, or the first; call it once the tool is ready
//   shell.panel.element / header / body   the side panel, for the tool to fill
//   shell.panel.following()            true while the views change size because the side panel opened or closed with
//                                      the selection: a view then keeps still, rather than keeping its middle in the
//                                      middle, and makes sure the selection isn't under the panel
//   shell.explorer.setLayouts(layouts, select)   fills the explorer: layouts are [{ name, label, title, nodes }], the
//                                      first the default. With more than one, a switch above the tree chooses, and the
//                                      choice is remembered as <tool>-explorer-layout. nodes are { label, id, detail,
//                                      badge, title, className, folder, children, expanded }; folder: true draws a
//                                      folder before the label. Choosing a node with an id calls select(id)
//   shell.selectionChanged(id)         call on every change of selection (null for none): the explorer marks the
//                                      item, opening what holds it, and the side panel opens or closes with it
//                                      unless it's locked open
//   shell.source.init(source)          says where the source text comes from (call it once, before start):
//     mode         "embedded" (in the page), "folder" (Load folder) or "none" (there's no source to show)
//     attribute    the attribute that holds each embedded source's id, such as "data-module"
//     files()      -> [{ id, file, hash }]: the files Load folder looks for, with the first 16 hex digits of the
//                  SHA-256 each had when the page was made
//     decode(buffer) -> the text of a loaded file
//     note         added to every Load folder status, such as a decoding problem; "" for none
//     highlight(lines) -> one HTML string per line
//     noFiles      the status when the chosen folder holds none of the files
//     mismatch     what a changed file means, completing "so …", such as "lines may not match"
//     loaded()     called once Load folder has read a folder
//   shell.source.has(id) / lines(id)   the source text, as an array of lines
//   shell.source.codeHtml(id, first, last, marked)   lines first to last (default: all) as the numbered, highlighted
//                                      code view, with a button that copies those lines; lines from marked.first to
//                                      marked.last are marked
//   A button with id "load-folder-prompt" anywhere in the page opens Load folder.
(function () {
  "use strict";

  function $(id) { return document.getElementById(id); }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"]/g, function (c) {
      return c === "&" ? "&amp;" : c === "<" ? "&lt;" : c === ">" ? "&gt;" : "&quot;";
    });
  }

  function create(options) {
    var tool = options.tool;

    // ---- Settings ----
    //
    // localStorage is shared by every page opened from disk, so each tool's settings start with its name. It can
    // throw when the page is opened from disk, so every access is guarded.

    var settings = {
      load: function (name) { try { return localStorage.getItem(tool + "-" + name); } catch (e) { return null; } },
      save: function (name, value) { try { localStorage.setItem(tool + "-" + name, value); } catch (e) { /* not remembered */ } },
      remove: function (name) { try { localStorage.removeItem(tool + "-" + name); } catch (e) { /* nothing stored */ } }
    };

    var listeners = { theme: [], view: [] };
    function emit(name, value) {
      listeners[name].forEach(function (fn) {
        try { fn(value); } catch (e) { console.error(e); }
      });
    }

    var data = JSON.parse($(tool + "-data").textContent);

    // ---- Theme ----

    var root = document.documentElement;
    var themeButton = $("theme-toggle");
    var systemLight = window.matchMedia ? window.matchMedia("(prefers-color-scheme: light)") : null;

    function currentTheme() { return root.getAttribute("data-theme") === "light" ? "light" : "dark"; }

    function showTheme() {
      var dark = currentTheme() === "dark";
      themeButton.setAttribute("aria-pressed", dark ? "true" : "false");
      themeButton.querySelector(".theme-label").textContent = dark ? "Dark" : "Light";
    }

    function applyTheme(theme) {
      if (theme === currentTheme()) return;
      root.setAttribute("data-theme", theme);
      showTheme();
      emit("theme", theme);
    }

    themeButton.addEventListener("click", function () {
      var next = currentTheme() === "dark" ? "light" : "dark";
      settings.save("theme", next); // the user's choice now wins over the system setting
      applyTheme(next);
    });

    if (systemLight && systemLight.addEventListener) {
      systemLight.addEventListener("change", function (e) {
        var stored = settings.load("theme");
        if (stored !== "light" && stored !== "dark") applyTheme(e.matches ? "light" : "dark");
      });
    }
    showTheme();

    // ---- Source text ----
    //
    // Embedded in the page (--embed-source), or read from the folder the user picks with Load folder, checking each
    // file against the hash taken when the page was made.

    var source = null;            // what the tool said in source.init
    var sources = new Map();      // id -> array of lines
    var highlighted = new Map();  // id -> array of highlighted HTML lines (cached)
    var statusBox = $("source-status");
    var statusText = $("source-status-text");
    var folderInput = $("load-folder");

    function splitLines(text) {
      var lines = text.split(/\r\n|\n|\r/);
      if (lines.length > 0 && lines[lines.length - 1] === "") lines.pop();
      return lines;
    }

    function setStatus(message, isWarning) {
      statusText.textContent = message;
      statusText.title = message;
      statusText.classList.toggle("warning", !!isWarning);
    }

    function initSource(config) {
      source = config;
      if (source.mode === "embedded") {
        statusBox.classList.add("embedded");
        document.querySelectorAll("pre." + tool + "-source").forEach(function (pre) {
          sources.set(pre.getAttribute(source.attribute), splitLines(pre.textContent));
        });
      } else if (source.mode === "folder") {
        setStatus("Source not loaded");
      } else {
        statusBox.hidden = true;
      }
    }

    function hex(buffer) {
      return Array.prototype.map.call(new Uint8Array(buffer), function (b) { return ("0" + b.toString(16)).slice(-2); }).join("");
    }

    // Reads the files from the folder the user picked. Only files directly in that folder count, so a .git folder or
    // other subfolders are ignored.
    function loadFolder(files) {
      var byName = new Map();
      Array.prototype.forEach.call(files, function (file) {
        var path = (file.webkitRelativePath || file.name).split("/");
        if (path.length <= 2) byName.set(path[path.length - 1].toLowerCase(), file);
      });

      var note = source.note || "";
      var missing = [];
      var changed = [];
      var found = 0;
      var reads = source.files().map(function (wanted) {
        var file = byName.get(wanted.file.toLowerCase());
        if (!file) { missing.push(wanted.file); return Promise.resolve(); }
        return file.arrayBuffer().then(function (buffer) {
          found++;
          sources.set(wanted.id, splitLines(source.decode(buffer)));
          highlighted.delete(wanted.id);
          if (!window.crypto || !crypto.subtle) return undefined;
          return crypto.subtle.digest("SHA-256", buffer).then(function (digest) {
            if (hex(digest).slice(0, 16) !== wanted.hash) changed.push(wanted.file);
          });
        });
      });

      return Promise.all(reads).then(function () {
        if (found === 0) {
          setStatus(source.noFiles, true);
        } else if (changed.length > 0) {
          setStatus(changed.length + " file(s) changed since this page was made, so " + source.mismatch + ". Run " + tool +
            " again. (" + changed.sort().join(", ") + ")" + note, true);
        } else if (missing.length > 0) {
          setStatus("Loaded " + found + " file(s); missing: " + missing.sort().join(", ") + note, true);
        } else {
          setStatus("Loaded " + found + " file(s)" + note, note !== "");
        }
        source.loaded();
      });
    }

    folderInput.addEventListener("change", function () {
      if (source && folderInput.files && folderInput.files.length > 0) loadFolder(folderInput.files);
    });

    // The side panel offers Load folder where the source would be.
    document.addEventListener("click", function (event) {
      if (event.target.closest && event.target.closest("#load-folder-prompt")) folderInput.click();
    });

    function highlightedLines(id) {
      if (!highlighted.has(id)) highlighted.set(id, source.highlight(sources.get(id)));
      return highlighted.get(id);
    }

    // The copy button sits in a row of no height above the code, which sticks to the top of what scrolls, and the
    // button sticks to its right, so it stays in view however far the code is scrolled either way.
    var COPY_ICON = "<svg class=\"icon copy-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><rect x=\"5.5\" y=\"5.5\" width=\"8.5\" height=\"8.5\" rx=\"1.5\"/><path d=\"M10.5 3.5V3A1.5 1.5 0 0 0 9 1.5H3.5A1.5 1.5 0 0 0 2 3v5.5A1.5 1.5 0 0 0 3.5 10H4\"/></svg>";
    var DONE_ICON = "<svg class=\"icon done-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M3 8.5l3.25 3.25L13 5\"/></svg>";

    function codeHtml(id, first, last, marked) {
      var lines = highlightedLines(id);
      first = first || 1;
      last = last || lines.length;
      var html = [];
      for (var n = first; n <= last; n++) {
        var mark = marked && n >= marked.first && n <= marked.last;
        html.push("<div class=\"line" + (mark ? " marked" : "") + "\" data-line=\"" + n + "\"><span class=\"ln\">" + n +
          "</span><span class=\"lt\">" + (lines[n - 1] || " ") + "</span></div>");
      }
      return "<div class=\"code-view\"><div class=\"code-tools\"><button type=\"button\" class=\"copy-code\" data-source=\"" +
        escapeHtml(id) + "\" data-first=\"" + first + "\" data-last=\"" + last + "\" title=\"Copy the code\" aria-label=\"Copy the code\">" +
        COPY_ICON + DONE_ICON + "<span class=\"copy-label\"></span></button></div>" +
        "<div class=\"code\" role=\"region\" aria-label=\"Source code\">" + html.join("") + "</div></div>";
    }

    // ---- Copying the source ----
    //
    // The lines shown, as the file has them, without line numbers. The clipboard API needs a secure context, which a
    // page opened from disk is in most browsers; failing that, the older copy command; failing both, the code is
    // selected for Ctrl+C.

    var announcer = $("announcer");

    function announce(text) {
      announcer.textContent = "";
      window.setTimeout(function () { announcer.textContent = text; }, 50);
    }

    function copyWithCommand(text) {
      var box = document.createElement("textarea");
      box.value = text;
      box.setAttribute("readonly", "");
      box.style.position = "fixed";
      box.style.top = "0";
      box.style.opacity = "0";
      var focused = document.activeElement;
      document.body.appendChild(box);
      box.select();
      var copied = false;
      try { copied = document.execCommand("copy"); } catch (e) { copied = false; }
      document.body.removeChild(box);
      if (focused && focused.focus) focused.focus();
      return copied;
    }

    function copyText(text) {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return copyWithCommand(text); });
      }
      return Promise.resolve(copyWithCommand(text));
    }

    function selectCode(button) {
      var code = button.closest(".code-view").querySelector(".code");
      var range = document.createRange();
      range.selectNodeContents(code);
      var selected = window.getSelection();
      selected.removeAllRanges();
      selected.addRange(range);
    }

    function showCopied(button, text) {
      button.classList.add("copied");
      button.querySelector(".copy-label").textContent = text;
      announce(text);
      window.clearTimeout(button.copiedTimer);
      button.copiedTimer = window.setTimeout(function () {
        button.classList.remove("copied");
        button.querySelector(".copy-label").textContent = "";
      }, 2000);
    }

    document.addEventListener("click", function (event) {
      var button = event.target.closest && event.target.closest(".copy-code");
      if (!button) return;
      var lines = sources.get(button.getAttribute("data-source")) || [];
      var text = lines.slice(Number(button.getAttribute("data-first")) - 1, Number(button.getAttribute("data-last"))).join("\n");
      copyText(text).then(function (copied) {
        if (copied) {
          showCopied(button, "Copied");
        } else {
          selectCode(button);
          showCopied(button, "Press Ctrl+C to copy");
        }
      });
    });

    // ---- Showing, hiding and resizing the panels ----
    //
    // The explorer sits before the views (left, or above them on a narrow screen) and the side panel after them (right,
    // or below). Every view shares both, so they all share each panel's size and whether it's shown. A panel's size is
    // its share of the layout (of its width, or of its height when the page is narrow and the panels are stacked), kept
    // as a CSS variable. Until the user resizes it, the stylesheets' defaults apply. The views need no telling: an SVG
    // fills its box and keeps its zoom, and a scrolling box scrolls.

    var layout = $("layout");
    var stacked = window.matchMedia ? window.matchMedia("(max-width: 900px)") : { matches: false };
    var KEY_STEP = 0.02;

    // config: element, splitter, toggle, close (ids); end: true when the panel is after the views; sizeVariable;
    // setting (stored as <setting>-size and <setting>-hidden); hiddenClass; min and max shares; visibleByDefault().
    function resizablePanel(config) {
      var element = $(config.element);
      var splitter = $(config.splitter);
      var toggle = $(config.toggle);

      function clampSize(size) { return Math.min(config.max, Math.max(config.min, size)); }

      function currentSize() {
        var rect = layout.getBoundingClientRect();
        var box = element.getBoundingClientRect();
        return stacked.matches ? box.height / rect.height : box.width / rect.width;
      }

      // size: a share of the layout, or null for the stylesheets' default.
      function setSize(size, remember) {
        if (size === null) {
          layout.style.removeProperty(config.sizeVariable);
        } else {
          size = clampSize(size);
          layout.style.setProperty(config.sizeVariable, (size * 100).toFixed(1) + "%");
        }
        if (remember) settings.save(config.setting + "-size", size === null ? "" : size.toFixed(3));
        updateSplitter();
      }

      function updateSplitter() {
        splitter.setAttribute("aria-orientation", stacked.matches ? "horizontal" : "vertical");
        if (!element.hidden) splitter.setAttribute("aria-valuenow", String(Math.round(currentSize() * 100)));
      }

      function setVisible(visible, remember) {
        element.hidden = !visible;
        splitter.hidden = !visible;
        layout.classList.toggle(config.hiddenClass, !visible);
        toggle.setAttribute("aria-pressed", visible ? "true" : "false");
        if (remember) settings.save(config.setting + "-hidden", visible ? "false" : "true");
        updateSplitter();
      }

      // Opening or closing by hand. A panel that doesn't remember whether it's shown (the side panel, which follows
      // the selection) tells the shell instead.
      function setByHand(visible) {
        setVisible(visible, config.rememberVisible !== false);
        if (config.changedByHand) config.changedByHand(visible);
      }

      toggle.addEventListener("click", function () { setByHand(element.hidden); });
      $(config.close).addEventListener("click", function () {
        setByHand(false);
        toggle.focus(); // the button that had focus has gone
      });

      // Dragging. The size is the distance from the pointer to the panel's own edge of the layout, less where in the
      // splitter it was grabbed. Without that, a click would move the splitter out from under the pointer, and the
      // second click of a double-click would miss it.
      var grabOffset = 0;

      function sizeAtPointer(event) {
        var rect = layout.getBoundingClientRect();
        if (stacked.matches) return config.end ? (rect.bottom - event.clientY) / rect.height : (event.clientY - rect.top) / rect.height;
        return config.end ? (rect.right - event.clientX) / rect.width : (event.clientX - rect.left) / rect.width;
      }

      splitter.addEventListener("pointerdown", function (event) {
        if (event.button !== 0) return;
        grabOffset = sizeAtPointer(event) - currentSize();
        splitter.setPointerCapture(event.pointerId);
        splitter.classList.add("dragging");
        document.body.classList.add("resizing");
        event.preventDefault();
      });
      splitter.addEventListener("pointermove", function (event) {
        if (splitter.hasPointerCapture(event.pointerId)) setSize(sizeAtPointer(event) - grabOffset, false);
      });
      function endDrag(event) {
        if (!splitter.hasPointerCapture(event.pointerId)) return;
        splitter.releasePointerCapture(event.pointerId);
        splitter.classList.remove("dragging");
        document.body.classList.remove("resizing");
        setSize(sizeAtPointer(event) - grabOffset, true);
      }
      splitter.addEventListener("pointerup", endDrag);
      splitter.addEventListener("pointercancel", endDrag);
      splitter.addEventListener("dblclick", function () { setSize(null, true); });

      // Keyboard, as for a separator: the arrows move the splitter, Home and End make the panel smallest or largest.
      splitter.addEventListener("keydown", function (event) {
        var grow = stacked.matches
          ? (config.end ? { ArrowUp: 1, ArrowDown: -1 } : { ArrowDown: 1, ArrowUp: -1 })
          : (config.end ? { ArrowLeft: 1, ArrowRight: -1 } : { ArrowRight: 1, ArrowLeft: -1 });
        var size;
        if (grow[event.key]) size = currentSize() + grow[event.key] * KEY_STEP;
        else if (event.key === "Home") size = config.min;
        else if (event.key === "End") size = config.max;
        else if (event.key === "Enter") size = null; // like double-click: back to the default
        else return;
        setSize(size, true);
        event.preventDefault();
      });

      if (stacked.addEventListener) stacked.addEventListener("change", updateSplitter);

      var storedSize = parseFloat(settings.load(config.setting + "-size"));
      setSize(isNaN(storedSize) ? null : storedSize, false);
      var storedHidden = config.rememberVisible === false ? null : settings.load(config.setting + "-hidden");
      setVisible(storedHidden === null ? config.visibleByDefault() : storedHidden !== "true", false);
      return { element: element, setVisible: setVisible, isVisible: function () { return !element.hidden; } };
    }

    // ---- The side panel follows the selection ----
    //
    // It opens when something is selected and closes when the selection is cleared, giving the views the room, unless
    // it's locked open. The lock is remembered per tool (<tool>-<panel>-locked); whether it's shown isn't, since the
    // selection decides. Closing it by hand also unlocks it, because a lock keeps it open.

    var lockButton = $("side-panel-lock");
    var locked = settings.load(options.panel + "-locked") === "true";
    var selected = null;

    function setLocked(on) {
      locked = on;
      lockButton.setAttribute("aria-pressed", on ? "true" : "false");
      lockButton.title = on
        ? "Unlock: close the panel when nothing is selected"
        : "Keep the panel open when nothing is selected";
      settings.save(options.panel + "-locked", on ? "true" : "false");
    }

    var sidePanel = resizablePanel({
      element: "side-panel", splitter: "side-panel-splitter", toggle: "side-panel-toggle", close: "side-panel-close",
      end: true, sizeVariable: "--side-panel-size", setting: options.panel, hiddenClass: "side-panel-hidden",
      min: 0.2, max: 0.7, visibleByDefault: function () { return locked; }, rememberVisible: false,
      changedByHand: function (visible) { if (!visible && locked) setLocked(false); }
    });
    var panel = sidePanel.element;
    lockButton.setAttribute("aria-pressed", locked ? "true" : "false");
    if (locked) lockButton.title = "Unlock: close the panel when nothing is selected";
    lockButton.addEventListener("click", function () { setLocked(!locked); });

    // Set while the views take their new size, which they're told of by a ResizeObserver in the next frame; cleared
    // the frame after.
    var following = false;

    function selectionChanged(id) {
      selected = id === undefined ? null : id;
      reveal(selected);
      if (locked || sidePanel.isVisible() === (selected !== null)) return;
      following = true;
      sidePanel.setVisible(selected !== null, false);
      window.requestAnimationFrame(function () {
        window.requestAnimationFrame(function () { following = false; });
      });
    }

    // Open by default on a wide screen, where there's room beside the views; on a narrow one it would push them down.
    resizablePanel({
      element: "explorer", splitter: "explorer-splitter", toggle: "explorer-toggle", close: "explorer-close",
      end: false, sizeVariable: "--explorer-size", setting: "explorer", hiddenClass: "explorer-hidden",
      min: 0.1, max: 0.4, visibleByDefault: function () { return !stacked.matches; }
    });

    // ---- Explorer tree ----
    //
    // The tool gives the tree as nodes: { label, id, detail, badge, title, className, folder, children, expanded }. A
    // node with an id is something the page documents, and choosing it calls the tool's select; one without is a
    // group, drawn as a folder when it is one. Groups start open and other nodes with children start closed. It's a
    // tree in the ARIA sense: one item is in the tab order, the arrow keys move through what's showing, and Enter
    // chooses. The tool can give more than one layout of the same items, such as folders and kinds.

    var treeBox = $("explorer-tree");
    var filterBox = $("explorer-filter");
    var emptyNote = $("explorer-empty");
    var layoutSwitch = $("explorer-layouts");
    var itemsById = new Map();
    var labels = new WeakMap();   // item -> its label in lower case, for the filter
    var selectItem = function () { };
    var selectedItem = null;
    var revealedId = null;        // what's selected, so a new layout can show it too
    var layouts = [];
    var activeLayout = null;

    // Drawn for this page rather than taken from an icon set, whose licences ask for attribution (see shared/README.md).
    var FOLDER_ICON = "<svg class=\"tree-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\">" +
      "<path d=\"M1.75 3.25h4.1l1.5 1.6h6.9v7.9H1.75z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linejoin=\"round\"/></svg>";

    function treeNode(node, depth) {
      var li = document.createElement("li");
      var isItem = node.id !== undefined && node.id !== null;
      li.setAttribute("role", "treeitem");
      li.setAttribute("aria-level", String(depth + 1));
      li.tabIndex = -1;
      li.className = (isItem ? "item" : "group") + (node.folder ? " folder" : "") + (node.className ? " " + node.className : "");
      labels.set(li, String(node.label).toLowerCase());

      // The tooltip is the row's, so it doesn't show over the rows inside a group.
      var row = document.createElement("div");
      row.className = "tree-row";
      if (node.title) row.title = node.title;
      row.style.setProperty("--depth", String(depth));
      row.innerHTML = "<span class=\"tree-twisty\" aria-hidden=\"true\"></span>" + (node.folder ? FOLDER_ICON : "") +
        "<span class=\"tree-label\">" + escapeHtml(node.label) + "</span>" +
        (node.detail ? "<span class=\"tree-detail\">" + escapeHtml(node.detail) + "</span>" : "") +
        (node.badge ? "<span class=\"tree-badge\">" + escapeHtml(node.badge) + "</span>" : "");
      li.appendChild(row);

      if (isItem) {
        li.dataset.id = node.id;
        li.setAttribute("aria-selected", "false");
        itemsById.set(node.id, li);
      }

      if (node.children && node.children.length > 0) {
        var open = node.expanded !== undefined ? node.expanded : !isItem;
        li.setAttribute("aria-expanded", open ? "true" : "false");
        li.classList.toggle("collapsed", !open);
        var group = document.createElement("ul");
        group.setAttribute("role", "group");
        node.children.forEach(function (child) { group.appendChild(treeNode(child, depth + 1)); });
        li.appendChild(group);
      }
      return li;
    }

    function setLayouts(given, select) {
      layouts = given;
      selectItem = select;
      var stored = settings.load("explorer-layout");
      activeLayout = layouts.some(function (l) { return l.name === stored; }) ? stored : layouts[0].name;
      layoutSwitch.innerHTML = layouts.map(function (l) {
        return "<button type=\"button\" role=\"radio\" data-layout=\"" + escapeHtml(l.name) + "\"" +
          (l.title ? " title=\"" + escapeHtml(l.title) + "\"" : "") + ">" + escapeHtml(l.label) + "</button>";
      }).join("");
      layoutSwitch.hidden = layouts.length < 2;
      showLayout();
    }

    // Draws the chosen layout, keeping the filter and what's selected.
    function showLayout() {
      var layout = layouts.filter(function (l) { return l.name === activeLayout; })[0];
      layoutSwitch.querySelectorAll("button").forEach(function (b) {
        b.setAttribute("aria-checked", b.dataset.layout === activeLayout ? "true" : "false");
      });
      itemsById.clear();
      selectedItem = null;
      treeBox.innerHTML = "";
      layout.nodes.forEach(function (node) { treeBox.appendChild(treeNode(node, 0)); });
      var first = treeBox.querySelector("li");
      if (first) first.tabIndex = 0;
      applyFilter();
      reveal(revealedId);
    }

    layoutSwitch.addEventListener("click", function (event) {
      var button = event.target.closest("button[data-layout]");
      if (!button || button.dataset.layout === activeLayout) return;
      activeLayout = button.dataset.layout;
      settings.save("explorer-layout", activeLayout);
      showLayout();
    });

    function hasChildren(li) { return li.hasAttribute("aria-expanded"); }

    function isOpen(li) { return li.getAttribute("aria-expanded") === "true" || li.classList.contains("open-for-filter"); }

    function setExpanded(li, open) {
      if (!hasChildren(li)) return;
      li.setAttribute("aria-expanded", open ? "true" : "false");
      li.classList.toggle("collapsed", !open);
    }

    function parentItem(li) {
      var parent = li.parentElement ? li.parentElement.closest("li") : null;
      return parent && treeBox.contains(parent) ? parent : null;
    }

    // What's showing: items not filtered out whose ancestors are all open, in order.
    function visibleItems() {
      return Array.prototype.filter.call(treeBox.querySelectorAll("li"), function (li) {
        if (li.hidden) return false;
        for (var at = parentItem(li); at; at = parentItem(at)) {
          if (at.hidden || !isOpen(at)) return false;
        }
        return true;
      });
    }

    function focusItem(li) {
      treeBox.querySelectorAll("li[tabindex=\"0\"]").forEach(function (other) { other.tabIndex = -1; });
      li.tabIndex = 0;
      li.focus();
    }

    function choose(li) {
      if (li.dataset.id !== undefined) selectItem(li.dataset.id);
      else setExpanded(li, !isOpen(li));
    }

    treeBox.addEventListener("click", function (event) {
      var li = event.target.closest("li");
      if (!li || !treeBox.contains(li)) return;
      focusItem(li);
      if (event.target.closest(".tree-twisty") && hasChildren(li)) setExpanded(li, !isOpen(li));
      else choose(li);
    });

    treeBox.addEventListener("keydown", function (event) {
      var li = event.target.closest("li");
      if (!li) return;
      var items = visibleItems();
      var at = items.indexOf(li);
      var handled = true;
      if (event.key === "ArrowDown") {
        if (at + 1 < items.length) focusItem(items[at + 1]);
      } else if (event.key === "ArrowUp") {
        if (at > 0) focusItem(items[at - 1]); else filterBox.focus();
      } else if (event.key === "Home") {
        focusItem(items[0]);
      } else if (event.key === "End") {
        focusItem(items[items.length - 1]);
      } else if (event.key === "ArrowRight") {
        if (hasChildren(li) && !isOpen(li)) setExpanded(li, true);
        else if (hasChildren(li) && items[at + 1] && parentItem(items[at + 1]) === li) focusItem(items[at + 1]);
      } else if (event.key === "ArrowLeft") {
        if (hasChildren(li) && isOpen(li) && !li.classList.contains("open-for-filter")) setExpanded(li, false);
        else if (parentItem(li)) focusItem(parentItem(li));
      } else if (event.key === "Enter" || event.key === " ") {
        choose(li);
      } else {
        handled = false;
      }
      if (handled) {
        event.preventDefault();
        event.stopPropagation();
      }
    });

    // The filter keeps every item whose name contains the text, with what holds it, opening that while it filters.
    // Clearing it puts back what was open before.
    function applyFilter() {
      var wanted = filterBox.value.trim().toLowerCase();
      treeBox.classList.toggle("filtering", wanted !== "");
      treeBox.querySelectorAll("li").forEach(function (li) {
        li.hidden = false;
        li.classList.remove("match", "open-for-filter");
      });
      if (wanted === "") {
        emptyNote.hidden = true;
        return;
      }

      var shown = 0;
      function filter(li) {
        var matches = labels.get(li).indexOf(wanted) >= 0;
        var childMatches = false;
        var group = li.querySelector(":scope > ul");
        if (group) {
          Array.prototype.forEach.call(group.children, function (child) { if (filter(child)) childMatches = true; });
        }
        li.hidden = !matches && !childMatches;
        li.classList.toggle("match", matches);
        li.classList.toggle("open-for-filter", childMatches);
        if (matches && li.dataset.id !== undefined) shown++;
        return matches || childMatches;
      }
      Array.prototype.forEach.call(treeBox.children, filter);
      emptyNote.hidden = shown > 0;
    }

    filterBox.addEventListener("input", applyFilter);
    filterBox.addEventListener("keydown", function (event) {
      if (event.key === "ArrowDown") {
        var first = visibleItems()[0];
        if (first) focusItem(first);
        event.preventDefault();
      } else if (event.key === "Escape" && filterBox.value !== "") {
        filterBox.value = "";
        applyFilter();
        event.stopPropagation(); // emptying the box isn't clearing the selection
      }
    });

    // Marks the selected item, opening what holds it and scrolling it into view, without taking the focus.
    function reveal(id) {
      revealedId = id === undefined ? null : id;
      if (selectedItem) selectedItem.setAttribute("aria-selected", "false");
      selectedItem = revealedId === null ? null : itemsById.get(revealedId) || null;
      if (!selectedItem) return;
      selectedItem.setAttribute("aria-selected", "true");
      for (var at = parentItem(selectedItem); at; at = parentItem(at)) setExpanded(at, true);
      treeBox.querySelectorAll("li[tabindex=\"0\"]").forEach(function (other) { other.tabIndex = -1; });
      selectedItem.tabIndex = 0;
      var row = selectedItem.querySelector(".tree-row");
      if (row && row.offsetParent !== null) row.scrollIntoView({ block: "nearest" });
    }

    // ---- Tabs ----

    var tabs = Array.prototype.slice.call(document.querySelectorAll(".tabs [role=\"tab\"]"));
    function viewOf(tab) { return tab.id.slice(4); } // "tab-graph" -> "graph"
    var activeView = viewOf(tabs[0]);

    function showView(name, focus) {
      activeView = name;
      tabs.forEach(function (tab) {
        var on = viewOf(tab) === name;
        tab.setAttribute("aria-selected", on ? "true" : "false");
        tab.tabIndex = on ? 0 : -1;
        $(tab.getAttribute("aria-controls")).hidden = !on;
        if (on && focus) tab.focus();
      });
      if (options.rememberView) settings.save("view", name);
      emit("view", name);
    }

    tabs.forEach(function (tab, index) {
      tab.addEventListener("click", function () { showView(viewOf(tab)); });
      tab.addEventListener("keydown", function (e) {
        if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
        var next = tabs[(index + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
        showView(viewOf(next), true);
        e.preventDefault();
      });
    });

    function start() {
      var stored = options.rememberView ? settings.load("view") : null;
      var known = tabs.some(function (tab) { return viewOf(tab) === stored; });
      showView(known ? stored : viewOf(tabs[0]));
    }

    return {
      data: data,
      escapeHtml: escapeHtml,
      settings: settings,
      theme: currentTheme,
      onThemeChange: function (fn) { listeners.theme.push(fn); },
      activeView: function () { return activeView; },
      showView: showView,
      onViewChange: function (fn) { listeners.view.push(fn); },
      start: start,
      panel: {
        element: panel, header: $("side-panel-header"), body: $("side-panel-body"),
        following: function () { return following; }
      },
      selectionChanged: selectionChanged,
      explorer: { setLayouts: setLayouts },
      source: {
        init: initSource,
        has: function (id) { return sources.has(id); },
        lines: function (id) { return sources.get(id); },
        codeHtml: codeHtml
      }
    };
  }

  window.ExcelDocumenterShell = { create: create };
})();
