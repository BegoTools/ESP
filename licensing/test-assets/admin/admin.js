"use strict";

const byId = id => document.getElementById(id);
const issue = byId("issue");
const edit = byId("edit");
const extend = byId("extend");
const licenseDialog = byId("license-dialog");
const editDialog = byId("edit-dialog");
const extendDialog = byId("extend-dialog");
const auth = "/api/admin/auth";
const api = "/api/admin/licenses";
const devicesApi = "/api/admin/devices";
const pageSize = 50;
const views = ["overview", "generator", "licenses", "devices"];
const lists = {
  licenses: { api, search: "", offset: 0, previous: byId("previous"), next: byId("next") },
  devices: { api: devicesApi, search: "", offset: 0, previous: byId("devices-previous"), next: byId("devices-next") }
};
const countFormat = new Intl.NumberFormat("en");
const dateFormat = new Intl.DateTimeFormat("ar-EG", { dateStyle: "medium" });
const dateTimeFormat = new Intl.DateTimeFormat("ar-EG", { dateStyle: "medium", timeStyle: "short" });
let view = "overview";
let accountEmail = "";
let session = 0;
let viewRequest = 0;
let drawerRequest = 0;
let currentLicense = null;

async function request(path, body) {
  const options = { cache: "no-store" };
  const requestSession = session;
  if (body !== undefined) {
    Object.assign(options, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  }
  const response = await fetch(path, options);
  if (!response.ok) {
    if (response.status === 401) {
      if (!path.startsWith(auth + "/")) {
        const error = new Error("الجلسة انتهت. سجل دخولك مرة تانية.");
        if (requestSession === session) {
          showAccount({ needsSetup: false, email: null });
          byId("auth-status").textContent = error.message;
        }
        throw error;
      }
      throw new Error(path.endsWith("/setup") ? "مفتاح الإعداد غير صحيح." : "الإيميل أو الباسورد غير صحيح.");
    }
    if (response.status === 409) {
      if (path.endsWith("/setup")) {
        showAccount({ needsSetup: false, email: null });
        throw new Error("حساب الإدارة موجود بالفعل. سجل دخولك.");
      }
      throw new Error("راجع الصلاحية وعدد الأجهزة. لو الحد الجديد أقل من الأجهزة المرتبطة، فك ربط الأجهزة الزائدة الأول.");
    }
    if (response.status === 429) throw new Error("فيه محاولات دخول كتير. استنى دقيقة وحاول تاني.");
    if (response.status === 400) throw new Error("راجع القيم المدخلة، خصوصًا تاريخ الانتهاء.");
    throw new Error("الخدمة غير متاحة حاليًا. حاول مرة أخرى.");
  }
  return response.json();
}

function element(tag, text = "", className = "") {
  const item = document.createElement(tag);
  item.textContent = text;
  item.className = className;
  return item;
}

function icon(name, className) {
  const item = element("span", "", className);
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  const use = document.createElementNS(svg.namespaceURI, "use");
  use.setAttribute("href", "#i-" + name);
  svg.append(use);
  item.setAttribute("aria-hidden", "true");
  item.append(svg);
  return item;
}

async function run(control, action, status = "status") {
  control.disabled = true;
  byId(status).textContent = "";
  try { await action(); }
  catch (error) { byId(status).textContent = error.message; }
  finally { control.disabled = false; }
}

function actionButton(text, action, variant = "quiet", status = "status") {
  const item = element("button", text, "button " + variant);
  item.type = "button";
  item.onclick = () => run(item, action, status);
  return item;
}

function timestamp(seconds, includeTime = false) {
  const date = new Date(seconds * 1000);
  const item = element("time", (includeTime ? dateTimeFormat : dateFormat).format(date));
  item.dateTime = date.toISOString();
  return item;
}

function identifier(value, length) {
  const item = element("bdi", value.slice(0, length), "identifier mono");
  item.title = value;
  return item;
}

function expiry(license) {
  return license.expires_at === null
    ? countFormat.format(license.duration_seconds / 86400) + " يوم من أول تفعيل"
    : timestamp(license.expires_at);
}

function licenseStatus(license) {
  if (license.revoked) return ["موقوف", "red"];
  if (license.expires_at !== null && license.expires_at * 1000 <= Date.now()) return ["منتهي", "amber"];
  if (license.expires_at === null) return ["بانتظار التفعيل", ""];
  return ["متاح", "green"];
}

function slots(license) {
  const description = license.used + " / " + license.device_limit;
  const item = element("div", "", "slots");
  const bar = element("progress");
  bar.max = license.device_limit;
  bar.value = license.used;
  bar.setAttribute("aria-label", "الأجهزة المرتبطة من الحد الأقصى");
  const count = element("span", description, "mono");
  count.dir = "ltr";
  item.append(count, bar);
  return item;
}

function renderTable(container, rows, headings, cells, emptyText) {
  if (!rows.length) {
    container.replaceChildren(element("p", emptyText, "empty"));
    return;
  }
  const table = element("table");
  const header = table.createTHead().insertRow();
  for (const heading of headings) {
    const cell = element("th", heading);
    cell.scope = "col";
    header.append(cell);
  }
  const body = table.createTBody();
  for (const row of rows) {
    const line = body.insertRow();
    for (const content of cells(row)) line.insertCell().append(content);
  }
  container.replaceChildren(table);
}

function renderLicenses(container, rows, emptyText) {
  renderTable(container, rows, ["الترخيص", "الأجهزة", "الصلاحية", "تاريخ الإصدار", "الحالة", "الإجراءات"], license => {
    const label = element("div");
    label.append(element("strong", license.label), identifier(license.id, 12));
    const [status, color] = licenseStatus(license);
    return [
      label, slots(license), expiry(license), timestamp(license.created_at), element("span", status, "badge " + color),
      actionButton("التفاصيل", () => openLicense(license.id))
    ];
  }, emptyText);
}

function renderDevices(container, rows, emptyText) {
  renderTable(container, rows, ["معرّف الجهاز", "الترخيص", "وقت الربط", "الإجراءات"], device => [
    identifier(device.device, 12), device.label, timestamp(device.activated_at, true),
    actionButton("فك الربط", () => unlinkDevice(device.license_id, device.device), "danger")
  ], emptyText);
}

function renderActivity(container, devices, isDrawer = false) {
  container.replaceChildren();
  if (!devices.length) container.append(element("p", "مفيش أجهزة مرتبطة.", "empty"));
  for (const device of devices) {
    const row = element("div", "", "activity-row");
    const details = element("div");
    if (!isDrawer) details.append(element("strong", device.label));
    details.append(identifier(device.device, 12));
    row.append(icon("device", "tile-icon cyan"), details, timestamp(device.activated_at, true));
    if (isDrawer) row.append(actionButton("فك الربط", () => unlinkDevice(currentLicense.id, device.device), "danger", "license-status"));
    container.append(row);
  }
}

function renderGeneratorHistory(licenses) {
  const container = byId("generator-recent");
  container.replaceChildren();
  if (!licenses.length) container.append(element("p", "لسه مفيش أكواد.", "empty"));
  for (const license of licenses.slice(0, 3)) {
    const row = element("div", "", "activity-row");
    const details = element("div");
    details.append(element("strong", license.label), identifier(license.id, 12));
    const [status, color] = licenseStatus(license);
    row.append(details, element("span", status, "badge " + color));
    container.append(row);
  }
}

function renderSummary(summary) {
  const stats = { ...summary, inactive: summary ? summary.total - summary.available : undefined };
  for (const item of document.querySelectorAll("[data-stat]")) {
    item.textContent = summary ? countFormat.format(stats[item.dataset.stat]) : "—";
  }
  byId("sidebar-available").textContent = summary ? countFormat.format(summary.available) : "—";
  byId("sidebar-total").textContent = summary ? countFormat.format(summary.total) : "—";
  byId("sidebar-meter").max = summary?.total || 1;
  byId("sidebar-meter").value = summary?.available || 0;
}

function listUrl(endpoint, search = "", offset = 0) {
  return endpoint + "?" + new URLSearchParams({ search, offset });
}

async function refresh() {
  if (!accountEmail) return;
  const id = ++viewRequest;
  const activeView = view;
  const list = lists[activeView];
  const query = list?.search;
  const offset = list?.offset;
  try {
    const summaryPromise = request("/api/admin/summary");
    if (activeView === "overview") {
      const [summary, licenses, devices] = await Promise.all([
        summaryPromise, request(listUrl(api)), request(listUrl(devicesApi))
      ]);
      if (id !== viewRequest) return;
      renderSummary(summary);
      renderLicenses(byId("recent-licenses"), licenses.slice(0, 5), "لسه مفيش أكواد.");
      renderActivity(byId("recent-devices"), devices.slice(0, 4));
    } else if (list) {
      const [summary, rows] = await Promise.all([summaryPromise, request(listUrl(list.api, query, offset))]);
      if (id !== viewRequest) return;
      renderSummary(summary);
      if (!rows.length && offset > 0) {
        list.offset = Math.max(0, offset - pageSize);
        await refresh();
        return;
      }
      const render = activeView === "licenses" ? renderLicenses : renderDevices;
      render(byId(activeView), rows.slice(0, pageSize), query ? "مفيش نتائج مطابقة للبحث." : "لسه مفيش سجلات.");
      byId(activeView + "-context").textContent = query ? "نتائج البحث عن «" + query + "»" : "الأحدث أولًا";
      byId(activeView + "-clear").hidden = !query;
      byId(activeView + "-page").textContent = "صفحة " + countFormat.format(offset / pageSize + 1);
      list.previous.disabled = offset === 0;
      list.next.disabled = rows.length <= pageSize;
    } else {
      const [summary, licenses] = await Promise.all([summaryPromise, request(listUrl(api))]);
      if (id !== viewRequest) return;
      renderSummary(summary);
      renderGeneratorHistory(licenses);
    }
  } catch (error) {
    if (id === viewRequest) byId("status").textContent = error.message;
  }
}

function showView() {
  const hash = location.hash.slice(1);
  view = views.includes(hash) ? hash : "overview";
  for (const name of views) {
    byId("view-" + name).hidden = name !== view;
    const link = document.querySelector('[data-view="' + name + '"]');
    if (name === view) link.setAttribute("aria-current", "page");
    else link.removeAttribute("aria-current");
  }
  const devices = view === "devices";
  const search = byId("search-text");
  search.value = lists[view]?.search || "";
  search.placeholder = devices ? "ابحث باسم الترخيص أو معرّف الجهاز…" : "بحث سريع عن الأكواد…";
  byId("status").textContent = "";
  void refresh();
}

function showAccount({ needsSetup, email }) {
  ++session;
  ++viewRequest;
  ++drawerRequest;
  accountEmail = email || "";
  byId("setup").hidden = !needsSetup;
  byId("login").hidden = needsSetup || Boolean(email);
  byId("auth-screen").hidden = Boolean(email);
  byId("panel").hidden = !email;
  byId("account-email").textContent = accountEmail;
  byId("auth-status").textContent = "";
  if (email) {
    showView();
    return;
  }
  for (const dialog of [extendDialog, editDialog, licenseDialog]) if (dialog.open) dialog.close();
  currentLicense = null;
  byId("code").value = "";
  byId("issued").hidden = true;
  byId("result-empty").hidden = false;
  issue.reset();
  issue.elements.mode.onchange();
  for (const name of ["licenses", "devices", "recent-licenses", "recent-devices", "generator-recent", "license-devices", "license-info"]) {
    byId(name).replaceChildren();
  }
  for (const [name, list] of Object.entries(lists)) {
    list.search = "";
    list.offset = 0;
    list.previous.disabled = list.next.disabled = true;
    byId(name + "-context").textContent = "الأحدث أولًا";
    byId(name + "-clear").hidden = true;
    byId(name + "-page").textContent = "";
  }
  byId("license-id").textContent = "";
  byId("license-label").textContent = "";
  edit.reset();
  renderSummary(null);
}

async function passwordVerifier(email, password) {
  const { salt } = await request(auth + "/salt", { email });
  const encoder = new TextEncoder();
  const key = await crypto.subtle.importKey("raw", encoder.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({
    name: "PBKDF2", hash: "SHA-256", iterations: 600000, salt: encoder.encode(salt)
  }, key, 256);
  return Array.from(new Uint8Array(bits), byte => byte.toString(16).padStart(2, "0")).join("");
}

async function signIn(event, formName) {
  event.preventDefault();
  const form = event.currentTarget;
  const email = byId(formName + "-email").value.trim();
  const password = byId(formName + "-password").value;
  await run(event.submitter, async () => {
    const settingUp = formName === "setup";
    if (settingUp && password !== byId("setup-confirm").value) throw new Error("الباسورد وتأكيده مختلفين.");
    byId("auth-status").textContent = "جاري التحقق…";
    const payload = { email, verifier: await passwordVerifier(email, password) };
    if (settingUp) payload.setupKey = byId("setup-key").value.trim();
    const account = await request(auth + "/" + formName, payload);
    form.reset();
    showAccount(account);
  }, "auth-status");
}

async function openLicense(id) {
  const pending = ++drawerRequest;
  const path = api + "/" + id;
  const [license, devices] = await Promise.all([request(path), request(path + "/devices")]);
  if (pending !== drawerRequest || !accountEmail) return;
  currentLicense = license;
  byId("license-id").textContent = license.id;
  byId("license-label").textContent = license.label;
  const [status, color] = licenseStatus(license);
  byId("license-badge").textContent = status;
  byId("license-badge").className = "badge " + color;
  const info = byId("license-info");
  info.replaceChildren();
  for (const [label, value] of [["الصلاحية", expiry(license)], ["تاريخ الإصدار", timestamp(license.created_at, true)]]) {
    const block = element("div");
    const description = element("dd");
    description.append(value);
    block.append(element("dt", label), description);
    info.append(block);
  }
  byId("license-slots").textContent = devices.length + " / " + license.device_limit;
  renderActivity(byId("license-devices"), devices, true);
  byId("license-revoke").textContent = license.revoked ? "إعادة تفعيل الكود" : "إيقاف الكود";
  byId("license-revoke").className = "button " + (license.revoked ? "quiet" : "danger");
  if (!licenseDialog.open) {
    byId("license-status").textContent = "";
    licenseDialog.showModal();
  }
}

async function changeLicense(id, action, payload) {
  await request(api + "/" + id + action, payload);
  const refreshView = refresh();
  if (licenseDialog.open && currentLicense?.id === id) {
    try { await openLicense(id); }
    catch (error) {
      if (licenseDialog.open) licenseDialog.close();
      await refreshView;
      byId("status").textContent = "تم الحفظ، لكن تعذّر تحديث التفاصيل. " + error.message;
      return;
    }
  }
  await refreshView;
}

async function unlinkDevice(id, device) {
  if (!confirm("تفك ربط الجهاز ده؟")) return;
  await changeLicense(id, "/devices", { device });
}

function validityFields(form, daysId, dateId) {
  form.elements.mode.onchange = () => {
    const mode = form.elements.mode.value;
    byId(daysId).hidden = form.elements.days.disabled = mode !== "days";
    byId(dateId).hidden = form.elements.date.disabled = mode !== "date";
  };
}

function licenseInput(form) {
  const { label, limit, mode, days, date } = form.elements;
  const data = { label: label.value, limit: Number(limit.value) };
  if (mode.value === "date") data.expiresAt = Math.floor(new Date(date.value).getTime() / 1000);
  if (mode.value === "days") data.days = Number(days.value);
  return data;
}

byId("login").onsubmit = event => signIn(event, "login");
byId("setup").onsubmit = event => signIn(event, "setup");
byId("logout").onclick = event => run(event.currentTarget, async () => {
  await request(auth + "/logout", {});
  showAccount({ needsSetup: false, email: null });
  byId("auth-status").textContent = "تم تسجيل الخروج.";
});

window.addEventListener("hashchange", () => {
  if (accountEmail) {
    showView();
    window.scrollTo(0, 0);
  }
});
document.addEventListener("keydown", event => {
  if (accountEmail && !document.querySelector("dialog[open]") && (event.ctrlKey || event.metaKey) && event.code === "KeyK") {
    event.preventDefault();
    byId("search-text").focus();
  }
});
byId("search").onsubmit = event => {
  event.preventDefault();
  const target = view === "devices" ? "devices" : "licenses";
  lists[target].search = byId("search-text").value.trim();
  lists[target].offset = 0;
  if (target === view) void refresh();
  else location.hash = target;
};
for (const [name, list] of Object.entries(lists)) {
  list.previous.onclick = () => { list.offset = Math.max(0, list.offset - pageSize); void refresh(); };
  list.next.onclick = () => { list.offset += pageSize; void refresh(); };
  byId(name + "-clear").onclick = () => {
    list.search = byId("search-text").value = "";
    list.offset = 0;
    void refresh();
  };
}

validityFields(issue, "days", "date");
validityFields(edit, "edit-days", "edit-date");
const presets = byId("validity-presets");
const updateIssueFields = issue.elements.mode.onchange;
issue.elements.mode.onchange = () => {
  updateIssueFields();
  presets.hidden = issue.elements.mode.value !== "days";
  issue.elements.days.oninput();
};
issue.elements.days.oninput = () => {
  for (const item of presets.children) {
    item.setAttribute("aria-pressed", String(item.dataset.preset === issue.elements.days.value));
  }
};
presets.onclick = event => {
  const preset = event.target.dataset.preset;
  if (!preset) return;
  issue.elements.days.value = preset;
  issue.elements.days.oninput();
};
for (const item of document.querySelectorAll("[data-quick-days]")) {
  item.onclick = () => {
    issue.elements.mode.value = "days";
    issue.elements.days.value = item.dataset.quickDays;
    issue.elements.mode.onchange();
    location.hash = "generator";
  };
}
issue.onsubmit = async event => {
  event.preventDefault();
  const requestSession = session;
  await run(event.submitter, async () => {
    const { code } = await request(api, licenseInput(issue));
    if (requestSession !== session) return;
    byId("code").value = code;
    byId("issued").hidden = false;
    byId("result-empty").hidden = true;
    byId("status").textContent = "تم إصدار الكود.";
    lists.licenses.search = "";
    lists.licenses.offset = 0;
    await refresh();
  });
};
byId("copy").onclick = async () => {
  try { await navigator.clipboard.writeText(byId("code").value); byId("status").textContent = "تم نسخ الكود."; }
  catch { byId("code").select(); }
};

byId("license-close").onclick = () => licenseDialog.close();
licenseDialog.addEventListener("close", () => { ++drawerRequest; currentLicense = null; });
byId("license-edit").onclick = () => {
  const license = currentLicense;
  edit.dataset.id = license.id;
  edit.elements.label.value = license.label;
  edit.elements.limit.value = license.device_limit;
  edit.elements.mode.value = "keep";
  edit.elements.mode.querySelector('[value="days"]').disabled = license.expires_at !== null;
  edit.elements.days.value = license.duration_seconds ? license.duration_seconds / 86400 : 30;
  edit.elements.date.value = "";
  edit.elements.mode.onchange();
  byId("edit-status").textContent = "";
  editDialog.showModal();
};
byId("license-extend").onclick = () => {
  extend.dataset.id = currentLicense.id;
  extend.reset();
  byId("extend-status").textContent = "";
  extendDialog.showModal();
};
byId("license-revoke").onclick = event => run(event.currentTarget, async () => {
  const license = currentLicense;
  await changeLicense(license.id, "/revoke", { revoked: !license.revoked });
  byId("license-status").textContent = license.revoked ? "تمت إعادة تفعيل الكود." : "تم إيقاف الكود.";
}, "license-status");

edit.onsubmit = async event => {
  event.preventDefault();
  await run(event.submitter, async () => {
    await changeLicense(edit.dataset.id, "", licenseInput(edit));
    editDialog.close();
    byId("license-status").textContent = "تم حفظ التعديلات.";
  }, "edit-status");
};
extend.onsubmit = async event => {
  event.preventDefault();
  await run(event.submitter, async () => {
    await changeLicense(extend.dataset.id, "/extend", { days: Number(extend.elements.days.value) });
    extendDialog.close();
    byId("license-status").textContent = "تم تمديد الصلاحية.";
  }, "extend-status");
};
byId("edit-cancel").onclick = () => editDialog.close();
byId("extend-cancel").onclick = () => extendDialog.close();

request(auth + "/status").then(showAccount).catch(error => { byId("auth-status").textContent = error.message; });
