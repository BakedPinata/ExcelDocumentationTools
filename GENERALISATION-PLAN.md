# Generalisation plan: C# solutions and Python projects

This plan widens the repository from Excel workbooks to source code in general. Two new tools document a C# solution
(its projects, types and members) and a Python project of many files, with the same page vbadoc makes: an explorer, a
dependency graph, a class diagram, the Calls tab and saved views. The repository, the shared library and the desktop
app get names that fit the wider goal. vbadoc and pqdoc keep their names and keep working as they do.

It is written to be handed to a fresh Claude Code session (or a developer) to carry out, as the
[feedback plan](FEEDBACK-PLAN.md) was.

**Status: draft, written on 2026-11-13, waiting for your review.** Nothing in it has been started. Section 1 lists
what to settle first; each question has a recommendation, and the rest of the plan assumes the recommendations.

## How to use this plan

- Read the whole file before starting. Sections 1–5 are the background and the decisions; section 6 has the steps.
- Work **one step at a time**, each on a branch of its own (`general/<step>`, such as `general/k1-names`), with a
  commit per change. At the end of each step:
  1. `dotnet build <root solution> -warnaserror` and the tests of everything touched pass;
  2. update the progress table below and `docs/WORKLOG.md`;
  3. raise the version of each tool the step changed;
  4. commit;
  5. summarise what changed and how to check it, then **stop and wait for review**;
  6. you rebase onto main and push after the review.
- Ask before doing anything that goes against this plan, a tool's `Requirements.md` or `DECISIONS.md`, or the
  guidelines. Section 7 lists the changes to those that this plan needs.
- Use Australian English in everything we write.
- A step marked **no visible change** is checked the way C1 and C2 were: every fixture's output compared byte for byte
  before and after, and screenshots of the pages compared pixel by pixel, with the build stamp made the same.

## Progress

| Step | Scope | Who | Status |
|---|---|---|---|
| [K0](#step-k0-settle-the-questions) | Settle the questions in section 1, and close the feedback plan | You, then Claude | Waiting for your review |
| [K1](#step-k1-names-inside-the-repository) | Names inside the repository: the root solution, the shared library, the shell's global, the publish folder, the documents. No visible change | Claude | Not started |
| [K2](#step-k2-the-desktop-apps-name) | The desktop app becomes `codedocui`, bringing its remembered settings with it | Claude, with VS Code closed | Not started |
| [K3](#step-k3-the-repositorys-name) | The repository's name on GitHub and on disk, `PATH`, and Claude's memory folder | You, then Claude | Not started |
| [L1](#step-l1-the-shared-library-in-three) | The shared library in three: what every tool needs, what only workbooks need, and the code views. The shell stops saying "workbook" where it means the source. No visible change | Claude | Not started |
| [L2](#step-l2-vbadocs-views-move-to-the-code-views-library) | vbadoc's graph, class diagram and Calls tab move to the code views library, unchanged. No visible change | Claude | Not started |
| [L3](#step-l3-the-code-model) | The code model: the views draw what a language describes (its kinds, its relationships, its words), and files are apart from the things in them. vbadoc is its first language. No visible change | Claude | Not started |
| [M1](#step-m1-spike-on-reading-a-c-solution) | Spike: read a C# solution with Roslyn and no build, on this repository and a real solution of yours | Claude, then you on a work machine | Not started |
| [M2](#step-m2-csdoc-reads-a-solution) | `csdoc`: the tool's skeleton, reading a solution into projects, namespaces, types and members | Claude | Not started |
| [M3](#step-m3-csdocs-dependencies) | `csdoc`: references with their lines, inheritance, interfaces, events, entry points and what's unreferenced | Claude | Not started |
| [M4](#step-m4-csdocs-page) | `csdoc`: the page, with the explorer, graph, class diagram, source with a C# highlighter, and doc comments | Claude, then you on a real solution | Not started |
| [M5](#step-m5-csdocs-calls) | `csdoc`: the Calls tab and sequence diagram | Claude | Not started |
| [M6](#step-m6-the-graph-at-group-level) | The graph at group level (projects, namespaces, packages, folders), in the code views, so every language has it | Claude | Not started |
| [M7](#step-m7-large-solutions) | Large solutions: the page stays usable at thousands of types | Claude, then you on a real solution | Not started |
| [N1](#step-n1-sample-python-project-and-a-spike-on-reading-it) | A sample Python project, and a spike on reading Python without running it | Claude, then you on a real project | Not started |
| [N2](#step-n2-pythondoc-reads-a-project) | `pythondoc`: the tool's skeleton, reading a project into packages, modules, classes and functions | Claude | Not started |
| [N3](#step-n3-pythondocs-dependencies) | `pythondoc`: imports, references with their lines, inheritance, and what couldn't be followed | Claude | Not started |
| [N4](#step-n4-pythondocs-page-and-calls) | `pythondoc`: the page, a Python highlighter, docstrings, and the Calls tab | Claude, then you on a real project | Not started |
| [P1](#step-p1-the-desktop-app-takes-solutions-and-projects) | The desktop app documents solutions and Python projects | Claude | Not started |
| [P2](#step-p2-finishing) | Finishing: the guidelines, the READMEs, and closing this plan | Claude | Not started |

## 1. Questions to settle

Each has a recommendation. Answer them in review, and K0 records the answers here.

### 1.1 Names

**The short name is still the product and the long name still the code** (the guidelines' rule). Recommended:

| | Now | Proposed |
|---|---|---|
| GitHub repository and local folder | `ExcelDocumentationTools` | `CodeDocumentationTools` |
| Root solution | `ExcelDocumentationTools.sln` | `CodeDocumentationTools.sln` |
| Publish folder | `…\source\Executables\ExcelDocumentationTools\` | `…\source\Executables\CodeDocumentationTools\` |
| Shared library | `ExcelDocumenter.Shared` | `CodeDocumenter.Shared`, `CodeDocumenter.Workbooks` and `CodeDocumenter.CodeViews` (see 5.2) |
| The shell's JavaScript global | `ExcelDocumenterShell` | `CodeDocumenterShell` |
| Desktop app | `xldocui`, `ExcelDocumenter.Desktop` | `codedocui`, `CodeDocumenter.Desktop` |
| Name kept for a combined tool | `xldoc` | `codedoc` |
| VBA tool | `vbadoc`, `VbaDocumenter` | unchanged |
| Power Query tool | `pqdoc`, `PowerQueryDocumenter` | unchanged |
| C# tool | none | `csdoc`, `CSharpDocumenter`, in `csdoc/`, writing `<solution>-csharp.html` |
| Python tool | none | `pythondoc`, `PythonDocumenter`, in `pythondoc/`, writing `<project>-python.html` |

- **vbadoc and pqdoc keep their names.** They say what each reads, scripts and `PATH` use them, and every remembered
  setting, saved views file and export manifest is keyed by them.
- **Not `pydoc`:** Python ships a command of that name, so ours would clash on any machine with Python on `PATH`.
- **"Code", not "Source":** the tools document code, wherever it's kept. `CodeDocumentationTools` also changes one
  word of the present name, so it's easy to recognise. If you'd rather have a product name than a description (an
  atlas, a map), say so in review and K1 uses it; it needs checking that nothing else has it.

### 1.2 The feedback plan

Recommended: **close it in K0**, as its section 8 sets out, since this plan changes the code its parked steps would
touch. What's still open moves here or to the tools' plans:

- G2–G4 (the views file built into the page, printing, copying a view as text) and E5b (the Calls tab's follow-ups)
  go to [Later](#9-later) in this plan. Built after phase L, they're built once for every language.
- Your checks in Excel (a bundle by hand, H3, H4 and J2) stay listed in `docs/WORKLOG.md` until done.
- Its Later section moves to vbadoc's and pqdoc's `docs/PLAN.md`.

The other choice is to keep both plans open. That's workable, but two plans touching `shared/` invite collisions.

### 1.3 How csdoc reads C#

Recommended: **Roslyn, with the compilations made by csdoc itself, and nothing built or run.** M1 is a spike that
tests this before anything is built on it. The choices:

| | How | For | Against |
|---|---|---|---|
| **A (recommended)** | Roslyn parses every file, and csdoc makes each project's compilation from what it reads in the solution and project files | Exact names within your own code: overloads, `var`, generics, extension methods, interfaces. Works with no SDK and no restore. Runs nothing | Reads project files itself, so it knows only the common settings (see 5.4). Types from NuGet packages are unknown unless the solution has been restored |
| B | Roslyn's MSBuild workspace loads the solution as Visual Studio does | Every setting right, every package known | Needs the .NET SDK and a restore on the machine; runs the build's own steps; slow; awkward in a single-file executable |
| C | Roslyn's parser only, with our own name lookup, as vbadoc has | Smallest | Guesses where A knows. C# leans on overloads and inference far more than VBA |

To answer in review, since they change what M1 tries:

1. Do the machines csdoc will run on have the .NET SDK, and are the solutions usually restored or built there?
2. Are any of the solutions older .NET Framework projects (project files that list every source file)?
3. Roughly how big are they: how many projects and source files in the largest?
4. Is anything in them not C# that should be read (VB.NET, F#, Razor, XAML)? The plan assumes not.

### 1.4 How pythondoc reads Python

Recommended: **our own light parser in C#, as vbadoc and pqdoc have**, tested by a spike (N1). The choices:

| | How | For | Against |
|---|---|---|---|
| **A (recommended)** | A tokeniser and structural parser of our own: indentation, strings, brackets, `import`, `class`, `def`, decorators, assignments, calls | Needs nothing installed. One self-contained executable, like the others | Ours to keep up with new syntax. It reads structure, not every expression |
| B | Run the machine's own Python with a small script that prints the syntax tree (`ast`) | An exact syntax tree, for no parsing work | Needs Python on the machine and allowed to run. A second process, and a second language in the repository |
| C | A parser library (ANTLR's Python grammar, or tree-sitter) | A full parse in .NET | A new dependency whose grammar trails the language, or native code inside the executable |

Whichever reads the syntax, **Python has no declared types, so following `thing.method()` to a definition is partly
inference** (see 5.5). The page counts what couldn't be followed, as vbadoc counts late-bound calls.

To answer in review: is the project one package, or several under one folder? Does it use type annotations much?
Annotations are the best evidence there is for what a name holds.

### 1.5 One set of views, or a copy for each language

Recommended: **one set**, in phase L. vbadoc's graph, class diagram and Calls tab are about 4,000 lines of
JavaScript, written in VBA's terms but not tied to VBA: a C# type or a Python class fills the place of a module, and
its members the place of members. Copying them twice would mean every later fix made three times. The cost is that
vbadoc's views are reworked before anything new shows, checked for no visible change.

The other choice is to copy first and merge later, as pqdoc's source panel was (B4, then C2). It shows a C# page
sooner and costs more overall.

### 1.6 Smaller questions

| Question | Recommended |
|---|---|
| Where a page goes by default | Beside the solution file, or in the Python project's root: source paths in the page are relative to that folder, so Load folder is given the same folder. `--output` changes it |
| Is source in the page? | No, as now: the page holds file names and hashes, and Load folder reads the files. `--embed-source` puts it in |
| The Export tab and `import` for C# and Python | No. They exist to move modules between workbooks; source files are already files |
| What the top bar's summary says in place of "Workbook saved" | Counts only. A page still never says when it was made. Showing the git commit is in Later |
| Test projects | Read like any other, with a `--exclude` option for projects, folders or files |
| Real code to check against | This repository for csdoc (about 200 source files, 5 projects and their tests), and one real solution and one real Python project of yours, kept in a git-ignored `private/` folder as vbadoc's workbook is |

## 2. Goals and non-goals

**Goals**

1. Document a C# solution: its projects, namespaces, types and members, what uses what and on which lines, what
   inherits and implements what, and the path a call takes.
2. Document a Python project of many files the same way: packages, modules, classes, functions and methods.
3. One page design and one set of views across VBA, C# and Python, so a feature built once reaches all three, and
   someone who knows one page knows the others.
4. Names that fit: the repository, the shared code and the desktop app stop saying Excel where they don't mean it.
5. vbadoc and pqdoc lose nothing: same commands, same outputs, same remembered settings, same saved views files.
6. The desktop app documents solutions and Python projects too.

**Non-goals**

- Building, restoring or running the code that's read. Nothing is executed, and nothing is fetched.
- API reference documentation, as DocFX and Sphinx make. These pages are for finding your way through code and how
  it hangs together; doc comments and docstrings are shown, not turned into a manual.
- Complete type inference for Python, or following reflection, dependency injection containers, `dynamic`, `getattr`
  or `eval`. The page says what couldn't be followed.
- Other languages (VB.NET, F#, TypeScript, notebooks), and links between languages. See [Later](#9-later).
- Changing source code. There's no Export tab and no `import` for the new tools.
- Anything that needs the network, an install or admin rights, as before.

## 3. Constraints carried over

From the guidelines and the feedback plan, all still true:

- Everything works from the `dotnet` command line, on .NET 10. Each tool publishes as one self-contained executable.
- A work machine has application control and no admin rights. Each new executable is tried there early (M1, N1).
- Pages load nothing from the network, and the same source gives the same page, byte for byte.
- Every dependency is compatible with the MIT licence. Check the licence and the current version when adding one.
- The repository is public, and real code of yours isn't. It stays in `private/` folders, and fixtures are written for
  the purpose or are this repository's own code.

## 4. What it will look like

### Commands

```
csdoc <solution, project or folder> [--output <folder>] [--output-name <name>] [--exclude <pattern>…]
                                    [--embed-source] [--on-exists …] [--force] [--verbose] [--quiet]
pythondoc <project folder>          the same options
```

- `csdoc` takes a `.sln`, `.slnx` or `.csproj`, or a folder holding exactly one solution.
- `pythondoc` takes the project's root folder. It leaves out virtual environments, `__pycache__`, build folders and
  anything under a folder whose name starts with a dot.
- Exit codes, logging and the shared options are the guidelines'.

### How each language fills the page

| | VBA (now) | C# | Python |
|---|---|---|---|
| Source | A workbook or add-in | A solution | A project folder |
| Groups, shown as folders | `'@Folder` | Projects, then namespaces | Packages (folders) |
| Units, the graph's upper level | Modules: standard, class, interface, document, form | Types: class, interface, struct, record, enum, delegate. A nested type is its own unit, named `Outer.Inner` | Modules (holding a file's own functions and variables) and classes |
| Members | Procedures, properties, variables, constants, enums, types, events, declares | Methods, constructors, properties, fields, events, indexers, operators, enum members | Functions, methods, properties, class and module variables |
| Relationships | Uses, Implements, Events | Uses, Inherits, Implements, Events | Uses, Inherits, Imports |
| Called without a reference in the code | Macros, worksheet functions, event handlers | `Main`, public members of a library, overrides, interface implementations, tests, members marked with attributes | `__main__` blocks, dunder methods, decorated functions, tests, public names of a package |
| Can't be followed | Late-bound calls | `dynamic`, reflection, unresolved names | Attribute calls on values of unknown type |
| Explorer layouts | Folders, Types | Projects, Namespaces, Types | Packages, Types |
| Outside code used | Library references | Packages and framework namespaces, per project | Imported packages that aren't the project's |

Everything the shell gives is there from the start: the explorer with its filter, favourites, hiding, widely used
items and the Show… menu, Uses / Used by with lines, saved views, both themes.

### Repository

```
shared/
  src/CodeDocumenter.Shared/        every tool: exit codes, version, files, command line, the page shell, d3
  src/CodeDocumenter.Workbooks/     vbadoc and pqdoc: opening and writing workbooks, importing
  src/CodeDocumenter.CodeViews/     vbadoc, csdoc and pythondoc: the code model, graph, class diagram, Calls
vbadoc/  pqdoc/                     as now
csdoc/                              CSharpDocumenter, .Cli, .Core, .Tests, .BrowserTests
pythondoc/                          PythonDocumenter, the same five
codedocui/                          the desktop app, renamed
```

## 5. Decisions

### 5.1 Order of the work

Names first (K), so new code is born with the right ones and the renames don't cut across half-built tools. Then the
shared views (L), because both new tools stand on them. Then C# (M) before Python (N): Roslyn gives exact answers, so
whatever goes wrong in M is the model's or the views' fault, not the reader's, and it's fixed before Python's guesses
arrive. The desktop app last (P), as it only runs what the tools can already do.

Each new tool begins with a spike that's tried on real code and on a work machine, as H1, I1 and J1 were. If a spike
says the recommended way won't do, the steps after it are rewritten before they're started.

### 5.2 The shared library in three

`ExcelDocumenter.Shared` holds three kinds of thing, and the new tools need only some of them:

- **`CodeDocumenter.Shared`:** what every tool needs. Exit codes, the build stamp, atomic and source folder writes,
  asking before overwriting, the command line's shared options, the page shell and d3.
- **`CodeDocumenter.Workbooks`:** what only vbadoc and pqdoc need. `Workbooks/` (with OpenMcdf, so csdoc and pythondoc
  don't carry it), `Importing/`, and the `import` command's options.
- **`CodeDocumenter.CodeViews`:** what vbadoc, csdoc and pythondoc need and pqdoc doesn't. The code model and the views
  drawn from it.

A tool's build stamp follows only the libraries it uses (`ToolStampPaths` already allows it), so a change to the code
views doesn't change pqdoc's pages. The guidelines' rule stays: code that only one tool needs stays in that tool.

### 5.3 The code model

The page's data, in terms no language owns. A language's Core fills it in; the views draw it.

- **Files** are apart from what's in them. A VBA module is one file, but a C# type can be spread over several
  (`partial`) and one file can hold several types, and a Python file holds a module and its classes. Each unit and
  member says which file and lines it's in; the source panel shows those lines, or the whole file.
- **Groups, units and members,** as the table in section 4 has them. Groups nest to any depth. Units are what the
  class diagram draws as boxes and the graph's upper level as nodes. Two levels with folders above is what vbadoc has
  now, and both new languages fit it, which is why the views can be shared without being rewritten.
- **Kinds are the language's:** each says what its kinds of unit, member and relationship are, with a label, a shape
  and a colour group for each, and the views take the legend, the Show… menu and the explorer's Types layout from
  that. New colours are validated with the dataviz palette validator, as the guidelines ask.
- **Ids are names:** `Namespace.Type` and `Namespace.Type.Member(parameter types)` in C#, `package.module.Class.method`
  in Python, a module's or member's name in VBA as now. Favourites, hidden items, the filter and saved views are kept
  by id, so an id only changes when the thing is renamed or moved.
- **Edges** are kept between members, with their lines, and rolled up to units and (from M6) groups.
- **Calls and blocks** are as vbadoc has them: each call in the order of the code, with the `if`, loop, `switch` or
  `try` around it.

vbadoc's page data changes shape once, in L3, to this. Its ids, its stored settings and its views files don't change.

### 5.4 Reading C#

Assuming M1 confirms choice A of 1.3:

- **The solution file** gives the projects (`.sln` and `.slnx`, read by our own code or by Microsoft's solution
  library, whichever M1 finds simpler). A `.csproj` alone is a solution of one.
- **A project's files** are every `.cs` under its folder, less `bin/`, `obj/` and what the project file removes; an
  older project file's own list is used as written.
- **A project's settings** come from its project file and the `Directory.Build.props` files above it, for the few
  that change what code means: the language version, `Nullable`, `ImplicitUsings`, defined constants, the root
  namespace, and references to other projects. Conditions and imports beyond that aren't evaluated; when one is
  met, the log says what was assumed.
- **References:** other projects in the solution, by their own compilations; the .NET reference assemblies, carried
  in the executable so no SDK is needed; and NuGet packages from `obj/project.assets.json` when a restore has left
  one. A type that can't be found doesn't stop anything: it's counted, and listed under `--verbose`.
- **Generated code** in `obj/` isn't read, apart from implicit usings, so what a source generator makes is unknown.
  Razor and XAML aren't read.
- **Multi-targeted projects** are read once, for the first target framework. `#if` branches follow that target.
- Roslyn is MIT licensed. It adds to the executable's size, which M1 measures.

### 5.5 Reading Python

Assuming N1 confirms choice A of 1.4:

- **Finding the project:** packages are folders with `__init__.py`, or any folder of `.py` files under the root; a
  `src/` layout is recognised. `pyproject.toml` gives the project's name when there is one.
- **Structure** is exact: imports, classes with their bases, functions with their parameters, annotations, decorators
  and docstrings, module and class variables, `self.` attributes set in a class's methods.
- **Names are followed by scope and import:** a local, an enclosing function, the module, then what it imported,
  `from x import *` included, by `__all__` when it's there.
- **What a name holds is inferred from the evidence in the code:** an annotation, an assignment from a class's
  constructor, a parameter's default, `self`, `cls` and `super()`. An attribute call on a value of unknown type isn't
  guessed at by name alone, since a method name such as `run` or `get` can be in many classes: it's recorded as one
  that couldn't be followed.
- **Imports are a relationship of their own,** between modules, and exact. Imports from outside the project are listed
  as the outside packages a module uses.
- New syntax is the parser's risk. The tokeniser has to know every string form, f-strings included; a statement the
  structural parser doesn't know is passed over, not an error.

### 5.6 What makes something an entry point

Each language's list of what's "called without a reference" (section 4's table) decides what's flagged as
unreferenced and where the Calls tab starts. For C# and Python the lists can't be complete, since frameworks call
code by attributes, decorators and naming conventions. So: only what can't be reached from outside is ever flagged
(private and internal members in C#, names starting with an underscore in Python), the page's wording is "no
reference found", and anything carrying an attribute or decorator is treated as called.

### 5.7 Size

A workbook has tens of modules; a solution can have thousands of types and tens of thousands of members. vbadoc is
tested to 300 modules, and its member-level graph was already slow at 70 (D1d). So:

- The graph opens at the coarsest level that's readable, which is why M6 adds the group level.
- The member level is offered only within a focus or filter once the solution is past a size M7 settles by measuring.
- The page's data is kept small: lines are numbers, repeated strings are stored once.
- M7 uses a generated solution, and your largest real one.

### 5.8 Renaming without breaking anything

- **Nothing a user has stored is keyed by a name that changes,** but for the desktop app's settings folder
  (`%LOCALAPPDATA%\xldocui`), which `codedocui` copies from on its first run if its own doesn't exist.
- **The pages' link to the repository** follows the new name. GitHub redirects the old address, so pages already made
  keep working.
- **Folder renames are done with VS Code closed,** from a terminal of its own: the C# extensions hold the tool
  folders. K2 is one of these.
- **Old executables** stay in the old publish folder until deleted by hand; K3 says so.

## 6. Steps

### Step K0: Settle the questions

Record the answers to section 1 in this file. If 1.2 is agreed, close the feedback plan as its section 8 sets out:
check its decisions are in the guidelines or a tool's `DECISIONS.md`, move its Later section, delete it in its own
commit, and point `docs/WORKLOG.md` and the tools' plans here.

### Step K1: Names inside the repository

The root solution, `ExcelDocumenter.Shared` (to `CodeDocumenter.Shared`, still one library until L1), the shell's
global, `ToolsPublishDir`, the build workflow, and every document that names them. **No visible change:** every
fixture's output is byte-identical apart from the build stamp. The link to the repository waits for K3.

### Step K2: The desktop app's name

`xldocui/` becomes `codedocui/`, `ExcelDocumenter.Desktop` becomes `CodeDocumenter.Desktop`, and its settings are
copied across on first run. Its title and README say what it now covers only once P1 has made it true; until then it
says it runs vbadoc and pqdoc. Done from a terminal outside VS Code.

### Step K3: The repository's name

Yours first, in this order, with a checklist written into this section when the step starts:

1. Rename the repository on GitHub.
2. Close VS Code, rename the local folder, and point the remote at the new address.
3. Copy Claude Code's memory folder for this repository to the name that matches the new path, or the next session
   starts without it.
4. Add the new publish folder to `PATH` on each machine, and delete the old one.

Then Claude: the pages' repository link, the READMEs, the build badge if any, and a check that a fresh clone builds,
tests and publishes.

### Step L1: The shared library in three

Split as 5.2 sets out. The shell's interface and messages say "source" where they said "workbook" and mean either
(the tool supplies the word shown, so vbadoc's and pqdoc's pages read as before); the Export tab becomes something a
tool asks for; the summary's saved time becomes optional. A views file or manifest written by vbadoc or pqdoc is
unchanged, `workbook` field included; the new tools write `source`, and both are read. **No visible change.**

### Step L2: vbadoc's views move to the code views library

`graph.js`, `classes.js`, `calls.js`, the parts of `page.js` and `page.css` they need, and `ClassDiagram` move as
they are, still speaking of modules. vbadoc's browser tests move with them where they test the views, and stay where
they test vbadoc. **No visible change.**

### Step L3: The code model

The model of 5.3, in C# and in the page's data, with vbadoc's Core filling it in. The views take kinds, labels,
shapes, relationships and wording from the language's description, and files are apart from units. A small made-up
language in the library's own tests (two kinds of unit, one extra relationship) proves the views don't assume VBA.
**No visible change to vbadoc's page;** its data changes shape, once.

### Step M1: Spike on reading a C# solution

`spikes/m1-csharp`, throwaway. It reads a solution as 5.4 sets out and reports: projects, types and members found;
the share of names resolved, and what the unresolved ones were; time and memory; the published executable's size.
Run on this repository, restored and not, and by you on a real solution and on a work machine. It also tries the
MSBuild workspace (choice B) on the same solutions, to say what A gives up. **The step ends with a recommendation,
and M2–M5 are revised to fit before they start.**

### Step M2: csdoc reads a solution

`csdoc/` with its solution, documents and publish profile, as the guidelines set out for a tool. A sample solution
as a fixture, written to cover the cases: several projects, nested and partial types, generics, overloads, records,
interfaces, events, extension methods, a multi-file type. `csdoc <solution>` prints a summary of what it found;
`--verbose` lists it. No page yet.

### Step M3: csdoc's dependencies

The dependency model: edges between members with their lines, Inherits, Implements and Events, entry points as 5.6
has them, unreferenced members, outside packages used, and the count of names that couldn't be followed. Tests
assert the sample's whole edge list, as vbadoc's do.

### Step M4: csdoc's page

`<solution>-csharp.html` on the shell and the code views: the explorer's three layouts, the graph at type and member
level, the class diagram, the source panel with a C# highlighter and Load folder, doc comment summaries in the side
panel, search. Browser tests and screenshots in both themes. Then your review on a real solution, which will add
follow-up steps, as D1's did.

### Step M5: csdoc's calls

Calls in order with the blocks around them; a call through an interface opens to its implementations, a virtual call
to its overrides; a call through a delegate or lambda is shown as one that couldn't be followed to a single place.
The sequence diagram comes with the Calls tab.

### Step M6: The graph at group level

A third level in the graph, above units: projects and namespaces in C#, packages in Python, `'@Folder` folders in
VBA. Edges are rolled up, and opening a group shows what's in it. In the code views, so it's built once.

### Step M7: Large solutions

Measure on a generated solution and your largest; settle the limits of 5.7; keep the hover and drag budgets vbadoc's
`SpeedTests` hold. The step's findings decide what, if anything, is left out at size, and the page says so when it is.

### Step N1: Sample Python project, and a spike on reading it

A sample project as a fixture: packages, relative and star imports, classes with inheritance, decorators, properties,
annotations and none, nested functions, every string form. `spikes/n1-python`, throwaway, tokenises and parses it and
a real project of yours, and reports what it read, what it passed over, and the share of attribute calls it could
follow. It's run on a work machine. **The step ends with a recommendation between 1.4's choices, and N2–N4 are
revised to fit.**

### Step N2: pythondoc reads a project

`pythondoc/`, set up as csdoc was. Project discovery, the tokeniser and structural parser, and a console summary.

### Step N3: pythondoc's dependencies

Imports, scopes, what names hold (5.5), edges with lines, Inherits and Imports, entry points, outside packages, and
what couldn't be followed. Tests assert the sample's whole edge list.

### Step N4: pythondoc's page and calls

`<project>-python.html`, as M4 and M5 together: the views are already there, so this is the language's description,
a Python highlighter, docstrings in the side panel, and the Calls tab's blocks (`if`, loops, `try`, `with`, `match`).
Then your review on a real project.

### Step P1: The desktop app takes solutions and projects

The Document tab offers each tool that can read what was picked: a workbook gives vbadoc and pqdoc, a solution csdoc,
a folder whichever of pqdoc, csdoc and pythondoc find something in it. The Import tab stays the Excel tools'. Its
test that every option is either offered or explained covers the new tools.

### Step P2: Finishing

Check every decision in section 5 is in `docs/GUIDELINES.md` or a tool's `docs/DECISIONS.md`, move
[Later](#9-later) into the relevant plans, update the root README, then delete this file in its own commit.

## 7. Changes to existing documents

To be made in the step that causes them, each asked about first, as the root `CLAUDE.md` requires:

- **`docs/GUIDELINES.md`:** the naming table gains the new tools and loses the old names (K1–K3, M2, N2);
  "Structure" describes three shared libraries and which tool uses which (L1); "Versions and the build stamp" says a
  stamp follows the libraries its tool uses (L1); "HTML pages" speaks of the source, not the workbook, and makes the
  Export tab the Excel tools' (L1).
- **vbadoc's `Requirements.md` and `DECISIONS.md`:** where its views now live, and the page data's new shape (L2, L3).
  Nothing about what vbadoc does changes.
- **pqdoc's and the desktop app's documents:** the new names only, until P1 widens the app's requirements.
- **New:** `Requirements.md`, `DECISIONS.md`, `PLAN.md`, `README.md` and `CLAUDE.md` for csdoc and pythondoc, written
  in M2 and N2; a README for each new shared library.
- **Root `README.md` and `CLAUDE.md`:** the new names (K1, K3) and the new tools as they arrive.

## 8. What you'll be asked to do

So the plan's calls on your time are in one place:

| When | What |
|---|---|
| Now | Review this plan and answer section 1 |
| K2 | Close VS Code while the app's folder is renamed |
| K3 | Rename the repository on GitHub and on disk, move Claude's memory folder, update `PATH` |
| M1 | Run the spike on a real solution, and on a work machine |
| M4, M7 | Try csdoc's page on a real solution; say what's missing or in the way |
| N1 | Run the spike on a real Python project, and on a work machine |
| N4 | Try pythondoc's page on a real project |
| Every step | Review, rebase onto main and push |

## 9. Later

Left out to keep this plan to what was asked for:

- **From the feedback plan, if 1.2 is agreed:** G2 (a views file in the output folder built into the page), G3
  (printing a view or every view, with descriptions), G4 (copy a view as text) and E5b (Ifs that seem to be missing
  from the Calls tab; keeping the sequence diagram's participants in view). Each is now built once for every language.
- **pqdoc on the code views,** if a query graph and a code graph turn out to want the same things.
- **One page for a repository that holds several languages,** and links between them (C# that starts a Python script,
  VBA that refreshes a query).
- **One executable** (`codedoc cs`, `codedoc py`, `codedoc vba`, `codedoc pq`).
- **The git commit in the top bar's summary,** read from `.git` without running git, if it can be kept deterministic.
- **Outside packages as nodes in the graph,** not only a list.
- **Other languages:** VB.NET (Roslyn reads it, so it's the cheapest), F#, TypeScript, Jupyter notebooks.
- **Python stub files (`.pyi`) and installed packages' types** as more evidence for what a name holds.
- **csdoc reading what source generators make,** from `obj/`, when it's there.
- **Names inside the source view as links,** in every tool.
