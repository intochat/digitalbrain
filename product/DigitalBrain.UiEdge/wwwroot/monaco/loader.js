// Minimal AMD loader stub for the first-cut Monaco surface. The real Monaco
// packages can replace these assets; the editor page always loads this path.
window.require = window.require || function require(deps, factory) {
  if (typeof factory === "function") {
    factory();
  }
};
window.define = window.define || function define() {};
