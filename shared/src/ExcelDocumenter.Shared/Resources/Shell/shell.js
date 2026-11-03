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
//                                      code view; lines from marked.first to marked.last are marked
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
      return "<div class=\"code\" role=\"region\" aria-label=\"Source code\">" + html.join("") + "</div>";
    }

    // ---- Showing, hiding and resizing the side panel ----
    //
    // One panel serves every view, so they all share its size and whether it's shown. The size is the panel's share
    // of the layout (of its width, or of its height when the page is narrow and the panel sits below the views), kept
    // as the --side-panel-size CSS variable. Until the user resizes it, the stylesheets' defaults apply. The views
    // need no telling: an SVG fills its box and keeps its zoom, and a scrolling box scrolls.

    var layout = $("layout");
    var panel = $("side-panel");
    var splitter = $("side-panel-splitter");
    var panelToggle = $("side-panel-toggle");
    var stacked = window.matchMedia ? window.matchMedia("(max-width: 900px)") : { matches: false };
    var MIN_PANEL_SIZE = 0.2;
    var MAX_PANEL_SIZE = 0.7;
    var KEY_STEP = 0.02;

    function clampSize(size) { return Math.min(MAX_PANEL_SIZE, Math.max(MIN_PANEL_SIZE, size)); }

    function currentPanelSize() {
      var rect = layout.getBoundingClientRect();
      var box = panel.getBoundingClientRect();
      return stacked.matches ? box.height / rect.height : box.width / rect.width;
    }

    // size: a share of the layout, or null for the stylesheets' default.
    function setPanelSize(size, remember) {
      if (size === null) {
        layout.style.removeProperty("--side-panel-size");
      } else {
        size = clampSize(size);
        layout.style.setProperty("--side-panel-size", (size * 100).toFixed(1) + "%");
      }
      if (remember) settings.save(options.panel + "-size", size === null ? "" : size.toFixed(3));
      updateSplitter();
    }

    function updateSplitter() {
      splitter.setAttribute("aria-orientation", stacked.matches ? "horizontal" : "vertical");
      if (!panel.hidden) splitter.setAttribute("aria-valuenow", String(Math.round(currentPanelSize() * 100)));
    }

    function setPanelVisible(visible, remember) {
      panel.hidden = !visible;
      splitter.hidden = !visible;
      layout.classList.toggle("side-panel-hidden", !visible);
      panelToggle.setAttribute("aria-pressed", visible ? "true" : "false");
      if (remember) settings.save(options.panel + "-hidden", visible ? "false" : "true");
      updateSplitter();
    }

    panelToggle.addEventListener("click", function () { setPanelVisible(panel.hidden, true); });
    $("side-panel-close").addEventListener("click", function () {
      setPanelVisible(false, true);
      panelToggle.focus(); // the button that had focus has gone
    });

    // Dragging. The panel is on the right (or at the bottom), so its size is the distance from the pointer to the
    // layout's far edge, less where in the splitter it was grabbed. Without that, a click would move the splitter out
    // from under the pointer, and the second click of a double-click would miss it.
    var grabOffset = 0;

    function sizeAtPointer(event) {
      var rect = layout.getBoundingClientRect();
      return stacked.matches ? (rect.bottom - event.clientY) / rect.height : (rect.right - event.clientX) / rect.width;
    }

    splitter.addEventListener("pointerdown", function (event) {
      if (event.button !== 0) return;
      grabOffset = sizeAtPointer(event) - currentPanelSize();
      splitter.setPointerCapture(event.pointerId);
      splitter.classList.add("dragging");
      document.body.classList.add("resizing");
      event.preventDefault();
    });
    splitter.addEventListener("pointermove", function (event) {
      if (splitter.hasPointerCapture(event.pointerId)) setPanelSize(sizeAtPointer(event) - grabOffset, false);
    });
    function endDrag(event) {
      if (!splitter.hasPointerCapture(event.pointerId)) return;
      splitter.releasePointerCapture(event.pointerId);
      splitter.classList.remove("dragging");
      document.body.classList.remove("resizing");
      setPanelSize(sizeAtPointer(event) - grabOffset, true);
    }
    splitter.addEventListener("pointerup", endDrag);
    splitter.addEventListener("pointercancel", endDrag);
    splitter.addEventListener("dblclick", function () { setPanelSize(null, true); });

    // Keyboard, as for a separator: the arrows move the splitter, Home and End make the panel smallest or largest.
    splitter.addEventListener("keydown", function (event) {
      var grow = stacked.matches ? { ArrowUp: 1, ArrowDown: -1 } : { ArrowLeft: 1, ArrowRight: -1 };
      var size;
      if (grow[event.key]) size = currentPanelSize() + grow[event.key] * KEY_STEP;
      else if (event.key === "Home") size = MIN_PANEL_SIZE;
      else if (event.key === "End") size = MAX_PANEL_SIZE;
      else if (event.key === "Enter") size = null; // like double-click: back to the default
      else return;
      setPanelSize(size, true);
      event.preventDefault();
    });

    if (stacked.addEventListener) stacked.addEventListener("change", updateSplitter);

    var storedSize = parseFloat(settings.load(options.panel + "-size"));
    setPanelSize(isNaN(storedSize) ? null : storedSize, false);
    setPanelVisible(settings.load(options.panel + "-hidden") !== "true", false);

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
      panel: { element: panel, header: $("side-panel-header"), body: $("side-panel-body") },
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
