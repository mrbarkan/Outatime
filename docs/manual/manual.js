// Shared by every language of the manual. English lives at manual/, the others at manual/<code>/.
const LANGS = [
  ["en", "English"], ["es", "Español"], ["pt-BR", "Português (Brasil)"], ["fr", "Français"], ["de", "Deutsch"],
  ["it", "Italiano"], ["ja", "日本語"], ["zh-Hans", "简体中文"],
];
const BASE = new URL(".", document.currentScript.src);
const CURRENT = document.documentElement.lang;
const KEY = "outatime-manual-lang";
const pageFor = code => new URL(code === "en" ? "" : code + "/", BASE).href + location.search;

// The browser's language, matched to a manual: "pt-PT" reads the Brazilian one, any Chinese the simplified one.
function preferred() {
  for (const tag of navigator.languages || [navigator.language]) {
    const t = tag.toLowerCase();
    const hit = LANGS.find(([c]) => c.toLowerCase() === t) || LANGS.find(([c]) => c.split("-")[0] === t.split("-")[0]);
    if (hit) return hit[0];
  }
  return "en";
}

// A language picked from the menu is remembered. Until then, the English page sends a browser set to another
// language to that manual; a manual opened by its own address (as the app does) stays put.
(() => {
  let chosen = null;
  try { chosen = localStorage.getItem(KEY); } catch {}
  const want = chosen || preferred();
  if (CURRENT === "en" && want !== "en" && LANGS.some(([c]) => c === want)) location.replace(pageFor(want));
})();

// ?theme=light|dark overrides the system appearance.
(() => {
  const t = new URLSearchParams(location.search).get("theme");
  if (t === "light" || t === "dark") document.documentElement.dataset.theme = t;
})();

document.addEventListener("DOMContentLoaded", () => {
  // Language menus: one in the header, one in the phone contents.
  document.querySelectorAll("select.lang").forEach(select => {
    select.innerHTML = LANGS.map(([c, name]) => `<option value="${c}" lang="${c}"${c === CURRENT ? " selected" : ""}>${name}</option>`).join("");
    select.addEventListener("change", () => {
      try { localStorage.setItem(KEY, select.value); } catch {}
      location.href = pageFor(select.value);
    });
  });

  // The hero's sample day, in hours since midnight. Labels come from the page, so each language names its activities.
  const track = document.getElementById("ribbon"), scale = document.getElementById("scale");
  const label = JSON.parse(track.dataset.labels);
  const day = [
    [9, 10.67, "work", "Acme"], [10.67, 10.92, "break"], [10.92, 12.5, "work", "Globex"],
    [12.5, 13.25, "lunch"], [13.25, 15.5, "work", "Acme"], [15.5, 16.25, "travel"],
    [16.25, 17.5, "work", "Acme"], [17.5, 17.75, "break"], [17.75, 19.6, "extra"],
  ];
  const from = 7, to = 21, pct = h => (h - from) / (to - from) * 100;
  for (let h = from + 1; h < to; h++) {
    track.insertAdjacentHTML("beforeend", `<div class="hour" style="left:${pct(h)}%"></div>`);
    scale.insertAdjacentHTML("beforeend", `<span style="left:${pct(h)}%">${String(h).padStart(2, "0")}:00</span>`);
  }
  day.forEach(([a, b, kind, client], i) => {
    // Breaks are too short on the ribbon for a label.
    const text = kind === "break" ? "" : client ? `${label[kind]} · ${client}` : label[kind];
    track.insertAdjacentHTML("beforeend",
      `<div class="block${i === day.length - 1 ? " now" : ""}" style="--c:var(--${kind});left:${pct(a)}%;width:${pct(b) - pct(a)}%;--d:${i * 0.07}s">${text ? `<span>${text}</span>` : ""}</div>`);
  });

  // Contents, built from the sections, and the current section highlighted while reading.
  const sections = [...document.querySelectorAll("main section")];
  const items = sections.map(s => `<li><a href="#${s.id}">${s.querySelector("h2").textContent}</a></li>`).join("");
  document.getElementById("toc").innerHTML = items;
  document.getElementById("toc-mobile").innerHTML = items;
  const links = new Map([...document.querySelectorAll("#toc a")].map(a => [a.hash.slice(1), a]));
  const seen = new IntersectionObserver(entries => {
    entries.forEach(e => {
      if (!e.isIntersecting) return;
      links.forEach(a => a.classList.remove("here"));
      links.get(e.target.id)?.classList.add("here");
    });
  }, { rootMargin: "-20% 0px -70% 0px" });
  sections.forEach(s => seen.observe(s));
});
