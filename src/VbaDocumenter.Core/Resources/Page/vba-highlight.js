// vba-highlight.js: a small VBA syntax highlighter for the source panel. No suitably licensed highlighting
// library supports VBA, so this one is built in. It marks keywords, strings, comments, numbers, the names of
// declared procedures/events, and type names (after As, New, Implements, and in Type/Enum declarations).
//
// Usage: VbaHighlight.highlightLines(arrayOfLines) returns one HTML string per line.
(function () {
  "use strict";

  var KEYWORDS = new Set((
    "alias and as attribute base binary byref byval call case close compare const declare defbool defbyte defcur " +
    "defdate defdbl defdec defint deflng deflnglng deflngptr defobj defsng defstr defvar dim do each else elseif " +
    "empty end enum eqv erase error event exit explicit false for friend function get global gosub goto if imp " +
    "implements in input is let lib like line lock loop lset me mod module new next not nothing null on open " +
    "option optional or output paramarray preserve print private property ptrsafe public put raiseevent random " +
    "redim rem resume return rset seek select set shared static step stop sub then to true type typeof unlock " +
    "until wend while with withevents write xor"
  ).split(" "));

  var TYPE_SUFFIXES = "%&!#@$^";
  var IDENTIFIER_START = /[A-Za-zÀ-￿]/;
  var IDENTIFIER_CHAR = /[A-Za-z0-9_À-￿]/;

  function escapeHtml(text) {
    return text.replace(/[&<>]/g, function (c) { return c === "&" ? "&amp;" : c === "<" ? "&lt;" : "&gt;"; });
  }

  function token(kind, text) {
    return "<span class=\"tok-" + kind + "\">" + escapeHtml(text) + "</span>";
  }

  // " _" at the end of a comment line continues the comment onto the next line, as in the VBA editor.
  function continues(text) {
    var trimmed = text.replace(/\s+$/, "");
    return trimmed === "_" || / _$/.test(trimmed) || /\t_$/.test(trimmed);
  }

  // End of a date literal such as #12:30:00 PM# starting at i, or -1. "#" is also a type suffix (x#) and a
  // file number (Print #1), so only accept it when it isn't attached to a name and looks like a date or time.
  function dateLiteralEnd(line, i) {
    if (i > 0 && /[A-Za-z0-9_)]/.test(line[i - 1])) return -1;
    var close = line.indexOf("#", i + 1);
    if (close < 0) return -1;
    var inner = line.slice(i + 1, close);
    return /\d/.test(inner) && /^[A-Za-z0-9\/:\-,. ]+$/.test(inner) ? close + 1 : -1;
  }

  function previousNonSpace(line, i) {
    for (var j = i - 1; j >= 0; j--) if (line[j] !== " " && line[j] !== "\t") return line[j];
    return "";
  }

  function highlightLine(line) {
    var html = "";
    var i = 0;
    var atStatementStart = true;
    var expect = null;          // "name" or "type": what the next identifier is
    var previousWord = "";      // last word, lower case
    var onlyVisibilitySoFar = true; // statement so far is empty or Public/Private (for "Private Type X")

    while (i < line.length) {
      var c = line[i];

      if (c === " " || c === "\t") { html += c; i++; continue; }

      if (c === "'" || (atStatementStart && /^rem(\s|$)/i.test(line.slice(i)))) {
        return { html: html + token("comment", line.slice(i)), commentContinues: continues(line) };
      }

      if (c === "\"") {
        var j = i + 1;
        while (j < line.length) {
          if (line[j] === "\"") {
            if (line[j + 1] === "\"") { j += 2; continue; } // "" is an escaped quote
            j++;
            break;
          }
          j++;
        }
        html += token("string", line.slice(i, j));
        i = j;
        atStatementStart = false;
        continue;
      }

      var dateEnd = c === "#" ? dateLiteralEnd(line, i) : -1;
      var isHex = c === "&" && /[hHoO]/.test(line[i + 1] || "") && /[0-9A-Fa-f]/.test(line[i + 2] || "");
      if (dateEnd > 0 || /[0-9]/.test(c) || isHex || (c === "." && /[0-9]/.test(line[i + 1] || "") && !/[A-Za-z0-9_)]/.test(previousNonSpace(line, i)))) {
        var end = dateEnd > 0 ? dateEnd : i + (isHex ? 2 : 1);
        if (dateEnd < 0) {
          while (end < line.length && /[0-9A-Fa-f.eE]/.test(line[end])) end++;
          if (end < line.length && TYPE_SUFFIXES.indexOf(line[end]) >= 0) end++;
        }
        html += token("number", line.slice(i, end));
        i = end;
        atStatementStart = false;
        continue;
      }

      if (IDENTIFIER_START.test(c)) {
        var k = i + 1;
        while (k < line.length && IDENTIFIER_CHAR.test(line[k])) k++;
        if (k < line.length && TYPE_SUFFIXES.indexOf(line[k]) >= 0 && !IDENTIFIER_CHAR.test(line[k + 1] || "")) k++;
        var word = line.slice(i, k);
        var lower = word.replace(/[%&!#@$^]$/, "").toLowerCase();
        var afterDot = previousNonSpace(line, i) === "."; // obj.End, .Value: a member name, not a keyword

        if (expect === "name" && !KEYWORDS.has(lower)) {
          html += token("name", word);
          expect = null;
        } else if (expect === "type" && !KEYWORDS.has(lower)) {
          html += token("type", word);
          expect = line[k] === "." ? "type" : null; // qualified names such as Excel.Range
        } else if (!afterDot && KEYWORDS.has(lower)) {
          html += token("keyword", word);
          var endsSomething = previousWord === "end" || previousWord === "exit";
          if ((lower === "sub" || lower === "function" || lower === "event") && !endsSomething) expect = "name";
          else if ((lower === "get" || lower === "let" || lower === "set") && previousWord === "property") expect = "name";
          else if ((lower === "type" || lower === "enum") && onlyVisibilitySoFar) expect = "type";
          else if (lower === "as" || lower === "new" || lower === "implements") expect = "type";
        } else {
          html += escapeHtml(word);
          expect = null;
        }

        if (lower !== "public" && lower !== "private") onlyVisibilitySoFar = false;
        previousWord = lower;
        i = k;
        atStatementStart = false;
        continue;
      }

      if (c === ":" && line[i + 1] !== "=") {
        atStatementStart = true;
        onlyVisibilitySoFar = true;
        previousWord = "";
        expect = null;
      } else if (c !== ".") {
        expect = expect === "type" && c === "(" ? null : expect;
      }
      html += escapeHtml(c);
      i++;
    }
    return { html: html, commentContinues: false };
  }

  function highlightLines(lines) {
    var result = [];
    var inContinuedComment = false;
    for (var n = 0; n < lines.length; n++) {
      if (inContinuedComment) {
        result.push(token("comment", lines[n]));
        inContinuedComment = continues(lines[n]);
        continue;
      }
      var highlighted = highlightLine(lines[n]);
      result.push(highlighted.html);
      inContinuedComment = highlighted.commentContinues;
    }
    return result;
  }

  window.VbaHighlight = { highlightLines: highlightLines };
})();
