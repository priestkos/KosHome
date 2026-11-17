// KosHome mock-up renderer: one copy of the app catalog and the tablet's layout rules, shared by every scene and the
// walkthrough so they cannot drift apart. Mirrors Host/AppCatalog.cs, MainWindow.cs and SettingsPage.cs.
(function () {
  const ICON_DIR = "../../../Assets/Apps/";

  // Host/AppCatalog.cs (Id, Name, Summary, Commands, HasSettings, Widget), in catalog (A-Z) order.
  const APPS = [
    { id: "Aetherpath", name: "Aetherpath", summary: "Prices every way to get where you're going - and tells you where your free and favored slots earn the most gil.", commands: "/aetherpath, /ap", settings: true },
    { id: "Calc14", name: "Calc14", summary: "A fast, pretty calculator.", commands: "/calc", settings: false },
    { id: "FrontlineAdvisor", name: "FrontlineAdvisor", summary: "Who is winning, who to hit, who is about to matter.", commands: "/fla", settings: true },
    { id: "GateAlert", name: "Gate Alert", summary: "Yells at you the moment your GATE opens in the Gold Saucer.", commands: "/gatealert, /gatetest", settings: true },
    { id: "KeyLock", name: "KeyLock", summary: "Moves movement off WASD and jump off Space on demand, and puts them back.", commands: "/keylock", settings: false },
    { id: "NameplateOffset", name: "Nameplate Offset", summary: "Raise or lower the nameplate above a character's head.", commands: "/npoffset", settings: false },
    { id: "Quill14", name: "Quill14", summary: "Roleplay posts: smart splitting, markup, templates and paced sending to Say, Party, Tell or Reply.", commands: "/quill", settings: true },
    { id: "VenueLedger", name: "VenueLedger", summary: "Every venue you know, on one clock.", commands: "/venue", settings: false },
    { id: "Wardrobe14", name: "Wardrobe14", summary: "A cross-collection view of your Penumbra mods; bulk toggles and Glamourer looks to follow.", commands: "/pmm", settings: false },
    { id: "WorldClock14", name: "WorldClock14", summary: "A home-screen widget: your timezones live, click one to copy it for chat. /tz converts any time.", commands: "/tz, /tzconfig", settings: true, widget: true },
  ];
  const DEFAULT_QUICK = ["Calc14", "Quill14", "Aetherpath", "WorldClock14"]; // Configuration.DefaultQuickApps

  // The sample world every scene shares: 2026-11-17 16:00 UTC, local zone IST.
  // Eorzea time from Motion.EorzeaTime (ms * 144 / 7 / 1000): 13:42, so the sun glyph (6 <= hour < 18).
  const SAMPLE = {
    time: "21:30", date: "Tuesday 17 November", et: "13:42", day: true,
    // WorldClock14 Defaults.CreateZones (IST, JST, UTC, ET) against the Local base; Delta = "+1d" when the day differs.
    zones: [["IST", "", "21:30"], ["JST", "+1d", "01:00"], ["UTC", "", "16:00"], ["ET", "", "11:00"]],
  };

  const BLOCKED = (id) => `The old ${id} plugin is still loaded. Disable it in /xlplugins, then press Retry.`; // KosHomePlugin.Start

  const esc = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  const icon = (id) => ICON_DIR + id + ".png";
  const app = (id) => APPS.find((a) => a.id === id);

  // MainWindow.Fit: the label cut with "..." when wider than the tile.
  const ctx = document.createElement("canvas").getContext("2d");
  ctx.font = "13.5px 'Segoe UI'";
  function fit(label, width) {
    if (ctx.measureText(label).width <= width) return label;
    for (let n = label.length - 1; n > 1; n--) if (ctx.measureText(label.slice(0, n) + "...").width <= width) return label.slice(0, n) + "...";
    return label.slice(0, 1);
  }

  function state(s) {
    s = s || {};
    const disabled = new Set(s.disabled || []);
    const status = s.status || {};
    const running = (a) => !disabled.has(a.id) && !status[a.id];
    return { disabled, status, running, open: new Set(s.open || []), toast: s.toast || null, quick: s.quick || DEFAULT_QUICK };
  }

  // ------------------------------------------------------------ chrome
  function wallpaper(w, h) {
    // Three glows: CheckMark, PlotLines (both Primary) and ButtonActive; 16 stacked circles of 0.016 * opacity alpha.
    const r = 0.42 * Math.min(w, h);
    const spots = [[0.5 + 0.12, 0.5 - 0.1, "255,179,173"], [0.5 - 0.15, 0.5 + 0.12, "255,179,173"], [0.5 + 0.05, 0.5 + 0.18, "111,71,68"]];
    return `<div class="wallpaper">${spots.map(([x, y, c]) =>
      `<div class="glow" style="left:${x * w - r}px;top:${y * h - r}px;width:${2 * r}px;height:${2 * r}px;background:radial-gradient(circle, rgba(${c},0.2) 0%, rgba(${c},0.1) 45%, rgba(${c},0) 100%)"></div>`).join("")}</div>`;
  }

  function statusBar(toast) {
    const island = toast
      ? `<div class="island"><img src="${icon(toast.id)}">${esc(app(toast.id).name + " " + toast.text)}</div>`
      : `<div class="island" style="width:66px"><span class="cam"></span></div>`;
    return `<div class="statusbar">
      <div class="round" style="left:4px;top:3px;width:18px;height:18px;font-size:11px" title="Fold into the quick launcher">&#x2921;</div>
      ${island}
      <div class="round" style="right:4px;top:3px;width:18px;height:18px;font-size:11px">&#x2715;</div>
    </div>`;
  }

  function dock(active) {
    const items = [["home", "&#x2302;"], ["apps", "&#x25A6;"], ["settings", "&#x2699;"]];
    return `<div class="dock-wrap"><div class="dock">${items.map(([k, g]) => `<div class="btn${k === active ? " active" : ""}">${g}</div>`).join("")}</div></div>`;
  }

  function tablet(opts, inner, active) {
    const w = opts.width || 460, h = opts.height || 650;
    return `<div class="tablet" id="${opts.id || "tablet"}" style="left:${opts.left}px;top:${opts.top}px;width:${w}px;height:${h}px">
      ${opts.wallpaper === false ? "" : wallpaper(w, h)}
      ${statusBar(opts.toast)}
      <div class="page${opts.scroll ? " scroll" : ""}">${inner}</div>
      ${dock(active)}
    </div>`;
  }

  // ------------------------------------------------------------ home
  function clockCard(st) {
    const running = APPS.filter(st.running).length;
    const pips = APPS.map((_, i) => `<span class="pip${i < running ? " on" : ""}"></span>`).join("");
    return `<div class="glass clock">
      <div class="time">${SAMPLE.time}</div>
      <div class="date">${SAMPLE.date}</div>
      <div class="et"><span class="sun">${SAMPLE.day ? "&#x2600;" : "&#x263E;"}</span>${SAMPLE.et}</div>
      <div class="etl">Eorzea</div>
      <div class="runl">${running} of ${APPS.length} running</div>
      <div class="pips">${pips}</div>
    </div>`;
  }

  function worldClockWidget(st) {
    const a = app("WorldClock14");
    if (st.disabled.has(a.id)) return "";
    if (st.status[a.id]) return `<div class="callout">${esc(a.name + ": " + st.status[a.id])}</div>`;
    return `<div class="glass widget">
      <div class="wc-head"><span class="wc-title">WORLD CLOCK</span>
        <span class="ghost">&#x29C9;</span><span class="ghost">&#x21C4;</span><span class="ghost">&#x2699;</span></div>
      <div class="wc-rows">${SAMPLE.zones.map(([z, d, t]) =>
        `<div class="wc-row"><span class="zl">${z}</span>${d ? `<span class="delta">${d}</span>` : ""}<span class="big">${t}</span></div>`).join("")}</div>
    </div>`;
  }

  function grid(st, width) {
    const cols = Math.min(6, Math.max(2, Math.floor(width / 96)));
    const cellW = width / cols;
    const ic = Math.min(72, Math.max(44, cellW * 0.6));
    const shown = APPS.filter((a) => !a.widget && !st.disabled.has(a.id)); // MainWindow.DrawGrid: off apps only on the Apps page
    if (shown.length === 0) return `<div class="disabled">Every app is switched off. Switch them on in Apps (the dock).</div>`;
    return `<div class="grid" style="grid-template-columns:repeat(${cols}, ${cellW}px)">${shown.map((a) => {
      const blocked = !st.running(a);
      const half = ic / 2;
      return `<div class="tile${blocked ? " dim" : ""}" style="height:${ic + 17 + 26}px">
        <img src="${icon(a.id)}" style="width:${ic}px;height:${ic}px">
        ${st.status[a.id] ? `<span class="warn" style="left:${cellW / 2 + half - 3 - 7}px;top:${8 + 3 - 7}px"></span>` : ""}
        <div class="lbl">${esc(fit(a.name, cellW - 8))}</div>
        ${st.open.has(a.id) ? `<div class="opendot"></div>` : ""}
      </div>`;
    }).join("")}</div>`;
  }

  function home(opts, s) {
    const st = state(s);
    const width = (opts.width || 460) - 16;
    return tablet({ ...opts, toast: st.toast }, clockCard(st) + worldClockWidget(st) + grid(st, width), "home");
  }

  // ------------------------------------------------------------ apps page
  function sw(on, label) {
    return `<span class="switch${on ? " on" : ""}"><span class="track"><span class="thumb"></span></span>${label ? `<span>${esc(label)}</span>` : ""}</span>`;
  }
  function section(title, num) {
    return `<div class="section"><span class="dia"></span>${num ? `<span class="num">${esc(num)}</span><span class="slash">&nbsp;/&nbsp;</span>` : ""}<span class="ttl">${esc(title.toUpperCase())}</span><span class="rule"></span></div>`;
  }
  function button(glyph, label) { return `<span class="button"><span class="g">${glyph}</span>${esc(label)}</span>`; }

  function appsPage(opts, s) {
    const st = state(s);
    const running = APPS.filter(st.running).length;
    const cards = APPS.map((a) => {
      const on = !st.disabled.has(a.id);
      const inst = st.running(a);
      const status = st.status[a.id];
      let below = "";
      if (status) below += `<div class="callout">${esc(status)}</div>` + (on && !inst ? `<div class="btns">${button("&#x21BB;", "Retry")}</div>` : "");
      if (inst) {
        const open = st.open.has(a.id);
        below += `<div class="btns">${button(open ? "&#x22A0;" : "&#x25A2;", open ? "Close" : "Open")}${a.settings ? button("&#x2699;", "Settings") : ""}</div>`;
      }
      return `<div class="card${status ? " warn" : ""}">
        <div class="row1"><img class="ico${inst ? "" : " dim"}" src="${icon(a.id)}">
          <div class="txt"><span>${esc(a.name)}</span> <span class="disabled">${esc(a.commands)}</span><div class="disabled">${esc(a.summary)}</div></div>
          ${sw(on)}</div>
        ${below}
      </div>`;
    }).join("");
    return tablet({ ...opts, scroll: true, toast: st.toast },
      section("Apps", `${running}/${APPS.length}`) + cards + `<div class="scrollbar" style="height:${opts.thumb || 150}px"></div>`, "apps");
  }

  // ------------------------------------------------------------ settings page
  function settingsPage(opts, s) {
    const st = state(s);
    const pct = opts.opacity ?? 85;
    const grabX = ((pct - 20) / (100 - 20)) * (240 - 12); // SliderFloat(Opacity, MinOpacity*100 .. 100)
    const quick = APPS.map((a) => `<div class="check${st.quick.includes(a.id) ? " on" : ""}"><span class="box"></span><img src="${icon(a.id)}"><span>${esc(a.name)}</span></div>`).join("");
    return tablet({ ...opts, scroll: false, toast: st.toast },
      section("Window", "01") +
      `<div class="row"><div class="slider"><span class="grab" style="left:${grabX}px"></span>${pct}%</div><span>Opacity</span><span class="help">(?)</span></div>` +
      `<div class="row">${sw(opts.frosted ?? true, "Frosted glass")}</div>` +
      `<div class="row">${sw(opts.wallpaperOn ?? true, "Glow wallpaper")}</div>` +
      section("Motion", "02") +
      `<div class="row">${sw(opts.animations ?? true, "Animations")}</div>` +
      section("Quick launcher", "03") +
      `<div class="disabled" style="margin-bottom:4px">The collapsed bar (the arrows, top left) shows these apps.</div>` +
      quick +
      `<div style="margin-top:6px">${button("&#x21B6;", "Reset look")}</div>`, "settings");
  }

  // ------------------------------------------------------------ app page (an app's window inside the tablet)
  function appPage(opts, id, body, tabs, active) {
    const a = app(id);
    const head = `<div class="apphead">
      <div class="round" style="position:relative;width:22px;height:22px;font-size:14px">&#x2039;</div>
      <img src="${icon(id)}"><span>${esc(a.name)}</span>
      <div class="round" style="right:0;top:0;width:22px;height:22px;font-size:12px">&#x2715;</div>
    </div>`;
    const tabRow = tabs ? `<div class="tabs">${tabs.map((t, i) => `<span class="tab${i === active ? " sel" : ""}">${esc(t)}</span>`).join("")}</div><div class="sep"></div>` : "";
    return tablet({ ...opts, toast: opts.toast }, head + tabRow + body, null); // Page.App: no dock button is active
  }

  // Apps/Calc14/CalculatorWindow.cs
  function calcBody(c) {
    const keys = ["AC", "CE", "&#x232B;", "&#xF7;", "7", "8", "9", "&#xD7;", "4", "5", "6", "&#x2212;", "1", "2", "3", "+", "%", "0", ".", "="];
    return `<div class="calc-sub">${esc(c.sub || "")}</div>
      <div class="calc-display">${esc(c.display)}</div>
      <div class="keys">${keys.map((k) => `<div class="key">${k}</div>`).join("")}</div>
      <div class="check on" style="margin-top:4px"><span class="box"></span><span>History</span></div>
      <div class="child">${c.history.length ? c.history.map((h) => `<div class="hist">${esc(h)}</div>`).join("") : `<div class="disabled">No calculations yet.</div>`}</div>
      ${button("", "Clear History").replace('<span class="g"></span>', "")}`;
  }

  window.KH = { APPS, SAMPLE, BLOCKED, home, appsPage, settingsPage, appPage, calcBody, icon, esc, section, sw, button };
})();
