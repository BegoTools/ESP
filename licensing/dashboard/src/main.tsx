import { useEffect, useRef, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { createRoot } from "react-dom/client";
import "./style.css";

type Row = Record<string, string | number | boolean | null>;
type Page = { items: Row[]; nextOffset: number | null };
type Actor = { email: string; role: "owner" | "support" };
type Overview = { actor: Actor; licenses: number; available: number; devices: number; recentlyActive: number; channels: Row[] };
type Settings = { maintenance: boolean; channels: Row[] };
type Field = {
  name: string; label: string; value?: string | number; options?: [string, string][];
  type?: string; optional?: boolean; min?: number; max?: number; hint?: string;
};
type Action = {
  title: string; method: string; path: string; fields?: Field[];
  description?: string; danger?: boolean; body?: (data: FormData) => object;
};

const copy: Record<string, string> = {
  "Cloud Control": "التحكم السحابي", Overview: "نظرة عامة", Licenses: "التراخيص", Devices: "الأجهزة",
  "Plans & features": "الباقات والمزايا", Plans: "الباقات", Features: "المزايا", Releases: "الإصدارات",
  "Audit & security": "التدقيق والأمان", Settings: "الإعدادات", Loading: "جاري التحميل…", Retry: "حاول مرة أخرى",
  "No results": "لا توجد نتائج", Search: "بحث", Sort: "الترتيب", Newest: "تنازلي", Oldest: "تصاعدي",
  Previous: "السابق", Next: "التالي", Actions: "الإجراءات", Cancel: "إلغاء", Save: "حفظ", Confirm: "تأكيد",
  Saved: "تم الحفظ", Copy: "نسخ", Copied: "تم النسخ", Close: "إغلاق", License: "الترخيص", Label: "الاسم",
  Limit: "الحد", Linked: "الأجهزة المرتبطة", Created: "تاريخ الإنشاء", Expiry: "تاريخ الانتهاء", Status: "الحالة",
  "First activation": "أول تفعيل", Revoked: "مُلغى", Active: "نشط", Expired: "منتهي", ID: "المعرّف",
  Plan: "الباقة", "No plan": "بدون باقة", "New license": "إنشاء ترخيص", Edit: "تعديل", Extend: "تمديد",
  Revoke: "إلغاء الترخيص", Restore: "إعادة التفعيل", Unlink: "فك الربط", Device: "الجهاز", Activated: "تاريخ التفعيل",
  "Last seen": "آخر اتصال", Never: "لا يوجد", "Days after first activation": "الأيام بعد أول تفعيل",
  "Fixed expiry (optional)": "تاريخ انتهاء ثابت (اختياري)", "Additional days": "الأيام الإضافية",
  "Copy the activation code now. It is displayed once.": "انسخ كود التفعيل الآن. سيظهر مرة واحدة.",
  "Unlink this device? It will lose access at the next verification.": "تفك ربط الجهاز؟ هيفقد الوصول عند التحقق القادم.",
  "Revoke this license? Its devices will lose access at the next verification.": "تلغي الترخيص؟ أجهزته هتفقد الوصول عند التحقق القادم.",
  "Restore this license? Its linked devices can verify again.": "تعيد تفعيل الترخيص؟ الأجهزة المرتبطة هتقدر تتحقق مرة تانية.",
  Available: "التراخيص المتاحة", "Recently active": "نشط مؤخرًا", "Verified in the last 15 minutes": "تم التحقق خلال آخر ١٥ دقيقة",
  Channel: "القناة", Current: "الحالي", Minimum: "الحد الأدنى", Maintenance: "الصيانة", "Global maintenance": "الصيانة العامة",
  "Edit channel": "تعديل القناة", "Save plan": "حفظ الباقة", "Save feature": "حفظ الميزة", "Plan ID": "معرّف الباقة",
  Name: "الاسم", Archived: "مؤرشفة", Feature: "الميزة", Scope: "النطاق", "Scope ID": "معرّف النطاق",
  Global: "عام", "Plan default": "افتراضي الباقة", Enabled: "مفعّلة", Disabled: "متوقفة", Inherit: "توريث", Value: "القيمة", "Set feature": "ضبط الميزة",
  Demo: "العرض التجريبي", Live: "التشغيل المباشر", Inspection: "فحص اللعبة", Version: "الإصدار", "Current version": "الإصدار الحالي",
  "Minimum supported version": "أقل إصدار مدعوم", "Register release": "تسجيل إصدار", "Manifest URL": "رابط بيان الإصدار",
  "Installer SHA-256": "بصمة المثبّت SHA-256", "Stable": "مستقرة", "Test": "تجريبية", Ready: "جاهز", Withdrawn: "مسحوب",
  "Set maintenance": "ضبط الصيانة", Off: "متوقفة", On: "مفعّلة", Actor: "الحساب", Action: "الإجراء", Entity: "الكيان",
  Time: "الوقت", Details: "التفاصيل", Owner: "المالك", Support: "الدعم", "Light mode": "وضع فاتح", "Dark mode": "وضع داكن",
  "License has no linked devices": "الترخيص غير مرتبط بأجهزة", "Request failed": "تعذر تنفيذ الطلب",
  "Access denied": "تم رفض الوصول", "Check your Cloudflare Access session.": "راجع تسجيل الدخول في Cloudflare Access.",
  "Check the submitted values.": "راجع القيم المدخلة.", "This change conflicts with existing data.": "التغيير يتعارض مع البيانات الحالية.",
  "The service is temporarily unavailable.": "الخدمة غير متاحة مؤقتًا.", "The requested item was not found.": "العنصر المطلوب غير موجود.",
  "Saving": "جاري الحفظ…", "Show linked devices": "عرض الأجهزة المرتبطة", "License ID": "معرّف الترخيص",
  "Leave blank to keep the existing expiry.": "اتركه فارغًا للإبقاء على تاريخ الانتهاء.",
  "Use the days field for a duration that starts at activation.": "استخدم حقل الأيام لتبدأ المدة عند التفعيل.",
  "Changing this channel can block clients below the minimum version.": "تغيير القناة قد يمنع العملاء الأقدم من الإصدار الأدنى."
};

const navigation = [
  ["overview", "Overview"], ["licenses", "Licenses"], ["devices", "Devices"], ["plans", "Plans & features"],
  ["releases", "Releases"], ["audit", "Audit & security"], ["system", "Settings"]
];
const errors: Record<string, string> = {
  unauthorized: "Check your Cloudflare Access session.", forbidden: "Access denied",
  bad_input: "Check the submitted values.", conflict: "This change conflicts with existing data.",
  service_unavailable: "The service is temporarily unavailable.", not_found: "The requested item was not found."
};
const roles = { owner: "Owner", support: "Support" };
const featureOptions: [string, string][] = [["demo", "Demo"], ["live", "Live"], ["inspection", "Inspection"]];
const channelOptions: [string, string][] = [["stable", "Stable"], ["test", "Test"]];
const switchOptions: [string, string][] = [["false", "Off"], ["true", "On"]];

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch("/v2/admin/" + path, { credentials: "same-origin", ...init });
  if (response.redirected || !response.headers.get("Content-Type")?.includes("application/json")) {
    throw new Error("Check your Cloudflare Access session.");
  }
  const value = await response.json();
  if (!response.ok) throw new Error(errors[value.error] || "Request failed");
  return value as T;
}

function useResource<T>(path: string, revision: number, retainValue = false) {
  const [value, setValue] = useState<T>();
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError("");
    if (!retainValue) setValue(undefined);
    api<T>(path, { signal: controller.signal }).then(result => {
      if (!controller.signal.aborted) setValue(result);
    }).catch(error => {
      if (!controller.signal.aborted) { setError(error.message); setValue(undefined); }
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [path, revision, attempt, retainValue]);
  return { value, error, loading, retry: () => setAttempt(attempt + 1) };
}

type Translate = (text: string) => string;
function Feedback({ resource, t }: { resource: { loading: boolean; error: string; retry: () => void }; t: Translate }) {
  if (resource.loading) return <p className="notice" role="status">{t("Loading")}</p>;
  if (!resource.error) return null;
  return <div className="notice failure" role="alert">{t(resource.error)} <button onClick={resource.retry}>{t("Retry")}</button></div>;
}

function Listing({ path, columns, sorts, revision, render, t }: {
  path: string; columns: string[]; sorts: [string, string][]; revision: number; render: (row: Row) => ReactNode; t: Translate;
}) {
  const [search, setSearch] = useState("");
  const [term, setTerm] = useState("");
  const [sort, setSort] = useState(sorts[0][0]);
  const [order, setOrder] = useState("desc");
  const [offset, setOffset] = useState(0);
  const query = new URLSearchParams({ search: term, sort, order, offset: String(offset), limit: "25" });
  const resource = useResource<Page>(path + "?" + query, revision);
  return <section className="panel">
    <div className="toolbar">
      <form className="search" onSubmit={e => { e.preventDefault(); setOffset(0); setTerm(search); }}>
        <input aria-label={t("Search")} placeholder={t("Search")} value={search} maxLength={100} onChange={e => setSearch(e.target.value)} />
        <button>{t("Search")}</button>
      </form>
      <label className="sort">{t("Sort")} <select value={sort} onChange={e => { setSort(e.target.value); setOffset(0); }}>
        {sorts.map(([key, title]) => <option key={key} value={key}>{t(title)}</option>)}
      </select></label>
      <select aria-label={t("Sort")} value={order} onChange={e => { setOrder(e.target.value); setOffset(0); }}>
        <option value="desc">{t("Newest")}</option><option value="asc">{t("Oldest")}</option>
      </select>
    </div>
    <Feedback resource={resource} t={t} />
    {resource.value && <>
      {resource.value.items.length ? <div className="table-wrap"><table>
        <thead><tr>{columns.map(column => <th scope="col" key={column}>{t(column)}</th>)}</tr></thead>
        <tbody>{resource.value.items.map(render)}</tbody>
      </table></div> : <p className="notice">{t("No results")}</p>}
      <div className="paging">
        <button disabled={!offset} onClick={() => setOffset(Math.max(0, offset - 25))}>{t("Previous")}</button>
        <button disabled={resource.value.nextOffset === null} onClick={() => setOffset(resource.value?.nextOffset ?? offset)}>{t("Next")}</button>
      </div>
    </>}
  </section>;
}

function Modal({ title, children, close, t }: { title: string; children: ReactNode; close: () => void; t: Translate }) {
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { dialog.current?.showModal(); }, []);
  return <dialog ref={dialog} onCancel={e => { e.preventDefault(); close(); }} aria-label={t(title)}>
    <div className="dialog-head"><h2>{t(title)}</h2><button onClick={close} aria-label={t("Close")}>×</button></div>
    {children}
  </dialog>;
}

function ActionDialog({ action, close, done, t }: { action: Action; close: () => void; done: (code?: string) => void; t: Translate }) {
  const [working, setWorking] = useState(false);
  const [error, setError] = useState("");
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    setWorking(true);
    setError("");
    try {
      const result = await api<{ code?: string }>(action.path, {
        method: action.method,
        headers: action.body ? { "Content-Type": "application/json" } : undefined,
        body: action.body ? JSON.stringify(action.body(data)) : undefined
      });
      done(result.code);
    } catch (error) {
      setError(error instanceof Error ? error.message : "Request failed");
    } finally {
      setWorking(false);
    }
  }
  return <Modal title={action.title} t={t} close={() => { if (!working) close(); }}>
    {action.description && <p>{t(action.description)}</p>}
    <form className="form" onSubmit={submit}>
      {action.fields?.map(field => <label key={field.name}>{t(field.label)}
        {field.options ? <select name={field.name} defaultValue={field.value}>
          {field.options.map(([value, title]) => <option key={value} value={value}>{t(title)}</option>)}
        </select> : <input name={field.name} type={field.type || "text"} defaultValue={field.value}
          maxLength={field.name === "manifestUrl" ? 250 : 100} min={field.min} max={field.max}
          required={!field.optional} aria-describedby={field.hint ? field.name + "-hint" : undefined} />}
        {field.hint && <small id={field.name + "-hint"}>{t(field.hint)}</small>}
      </label>)}
      {error && <p className="failure" role="alert">{t(error)}</p>}
      <div className="dialog-actions">
        <button type="button" disabled={working} onClick={close}>{t("Cancel")}</button>
        <button disabled={working} className={action.danger ? "danger" : "primary"}>
          {t(working ? "Saving" : action.fields ? "Save" : "Confirm")}
        </button>
      </div>
    </form>
  </Modal>;
}

const fieldText = (data: FormData, key: string) => String(data.get(key) ?? "").trim();
const numberField = (name: string, label: string, value: number, min: number, max: number): Field => ({ name, label, value, min, max, type: "number" });

function licenseAction(row?: Row): Action {
  const expiry: Field = { name: "expires", label: "Fixed expiry (optional)", type: "datetime-local", optional: true,
    hint: row ? "Leave blank to keep the existing expiry." : "Use the days field for a duration that starts at activation." };
  const fields: Field[] = [
    { name: "label", label: "Label", value: String(row?.label ?? "") },
    numberField("limit", "Limit", Number(row?.device_limit ?? 1), 1, 100),
    { name: "plan", label: "Plan ID", value: String(row?.plan_id ?? ""), optional: true },
    expiry
  ];
  if (!row || row.expires_at === null) fields.splice(3, 0, {
    ...numberField("days", "Days after first activation", row ? Number(row.duration_seconds) / 86400 : 30, 1, 3650), optional: true
  });
  return {
    title: row ? "Edit" : "New license", method: row ? "PATCH" : "POST", path: row ? "licenses/" + row.id : "licenses", fields,
    body: data => {
      const fixed = fieldText(data, "expires");
      const days = fieldText(data, "days");
      const body = { label: fieldText(data, "label"), limit: Number(data.get("limit")), plan: fieldText(data, "plan") || null };
      if (fixed) return { ...body, expiresAt: Math.floor(new Date(fixed).getTime() / 1000) };
      return days ? { ...body, days: Number(days) } : body;
    }
  };
}

function planAction(row?: Row): Action {
  return {
    title: "Save plan", method: "PUT", path: "plans", fields: [
      { name: "id", label: "Plan ID", value: String(row?.id ?? "") },
      { name: "name", label: "Name", value: String(row?.name ?? "") },
      numberField("maxDevices", "Limit", Number(row?.max_devices ?? 1), 1, 100),
      { name: "status", label: "Status", value: String(row?.status ?? "active"), options: [["active", "Active"], ["archived", "Archived"]] }
    ],
    body: data => ({ id: fieldText(data, "id"), name: fieldText(data, "name"), status: data.get("status"), maxDevices: Number(data.get("maxDevices")) })
  };
}

function featureAction(row?: Row): Action {
  return {
    title: "Save feature", method: "PUT", path: "features", fields: [
      { name: "key", label: "Feature", value: String(row?.feature_key ?? "demo"), options: featureOptions },
      { name: "scope", label: "Scope", value: String(row?.scope_type ?? "global"), options: [["global", "Global"], ["plan_default", "Plan default"], ["plan", "Plan"], ["license", "License"]] },
      { name: "id", label: "Scope ID", value: String(row?.scope_id ?? ""), optional: true },
      { name: "enabled", label: "Value", value: String(row?.value_json ?? "true"), options: [["true", "Enabled"], ["false", "Disabled"], ["null", "Inherit"]] }
    ],
    body: data => ({ key: data.get("key"), scope: data.get("scope"), id: fieldText(data, "id"), enabled: JSON.parse(fieldText(data, "enabled")) })
  };
}

const releaseAction: Action = {
  title: "Register release", method: "POST", path: "releases",
  fields: [{ name: "version", label: "Version" }, { name: "channel", label: "Channel", options: channelOptions },
    { name: "manifestUrl", label: "Manifest URL", type: "url" }, { name: "checksum", label: "Installer SHA-256" }],
  body: data => Object.fromEntries(Array.from(data.keys(), key => [key, fieldText(data, key)]))
};

function channelAction(row: Row): Action {
  return {
    title: "Edit channel", method: "PUT", path: "system",
    description: "Changing this channel can block clients below the minimum version.",
    fields: [
      { name: "current", label: "Current version", value: String(row.current_version ?? ""), optional: true },
      { name: "min", label: "Minimum supported version", value: String(row.min_supported_version) },
      { name: "maintenance", label: "Maintenance", value: String(Boolean(row.maintenance)), options: switchOptions }
    ],
    body: data => ({ channel: row.channel, currentVersion: fieldText(data, "current") || null,
      minSupportedVersion: fieldText(data, "min"), maintenance: data.get("maintenance") === "true" })
  };
}

function ChannelTable({ items, t, edit }: { items: Row[]; t: Translate; edit?: (action: Action) => void }) {
  return <div className="table-wrap"><table>
    <thead><tr>{["Channel", "Current", "Minimum", "Maintenance", ...(edit ? ["Actions"] : [])].map(key => <th scope="col" key={key}>{t(key)}</th>)}</tr></thead>
    <tbody>{items.map(row => <tr key={String(row.channel)}>
      <td>{t(row.channel === "stable" ? "Stable" : "Test")}</td><td>{row.current_version ?? "—"}</td><td>{row.min_supported_version}</td>
      <td><span className={"pill " + (row.maintenance ? "warn" : "")}>{t(row.maintenance ? "On" : "Off")}</span></td>
      {edit && <td><button onClick={() => edit(channelAction(row))}>{t("Edit channel")}</button></td>}
    </tr>)}</tbody>
  </table></div>;
}

function SystemPanel({ revision, t, edit }: { revision: number; t: Translate; edit: (action: Action) => void }) {
  const resource = useResource<Settings>("system", revision);
  return <section className="panel">
    <Feedback resource={resource} t={t} />
    {resource.value && <>
      <div className="toolbar"><strong>{t("Global maintenance")}</strong><span className={"pill " + (resource.value.maintenance ? "warn" : "")}>
        {t(resource.value.maintenance ? "On" : "Off")}
      </span><button onClick={() => edit({
        title: "Set maintenance", method: "PUT", path: "system",
        fields: [{ name: "maintenance", label: "Maintenance", options: switchOptions, value: String(resource.value?.maintenance) }],
        body: data => ({ maintenance: data.get("maintenance") === "true" })
      })}>{t("Set maintenance")}</button></div>
      <ChannelTable items={resource.value.channels} t={t} edit={edit} />
    </>}
  </section>;
}

function App() {
  const [language, setLanguage] = useState(localStorage.getItem("sv-language") === "ar" ? "ar" : "en");
  const [light, setLight] = useState(localStorage.getItem("sv-theme") === "light");
  const [section, setSection] = useState(location.hash.slice(1) || "overview");
  const [revision, setRevision] = useState(0);
  const [action, setAction] = useState<Action>();
  const [code, setCode] = useState("");
  const [toast, setToast] = useState("");
  const [deviceFilter, setDeviceFilter] = useState("");
  const resource = useResource<Overview>("overview", revision, true);
  const actor = resource.value?.actor;
  const owner = actor?.role === "owner";
  const t = (key: string) => language === "ar" ? copy[key] || key : key;
  const timestamp = (value: Row[string], empty = "Never") => value ? new Date(Number(value) * 1000).toLocaleString(language === "ar" ? "ar-EG" : "en-GB") : t(empty);
  useEffect(() => {
    document.documentElement.lang = language;
    document.documentElement.dir = language === "ar" ? "rtl" : "ltr";
    document.documentElement.dataset.theme = light ? "light" : "dark";
    localStorage.setItem("sv-language", language);
    localStorage.setItem("sv-theme", light ? "light" : "dark");
  }, [language, light]);
  useEffect(() => {
    const change = () => setSection(location.hash.slice(1) || "overview");
    addEventListener("hashchange", change);
    return () => removeEventListener("hashchange", change);
  }, []);
  useEffect(() => {
    if (!toast) return;
    const timeout = setTimeout(() => setToast(""), 3500);
    return () => clearTimeout(timeout);
  }, [toast]);

  function button(label: string, selected: Action, style?: string) {
    return <button className={style} onClick={() => setAction(selected)}>{t(label)}</button>;
  }

  const selected = navigation.find(([key]) => key === section && (owner || !["audit", "system"].includes(key))) || navigation[0];
  let content: ReactNode;
  if (selected[0] === "licenses") {
    content = <>
      {owner && <div className="section-actions">{button("New license", licenseAction(), "primary")}</div>}
      <Listing path="licenses" t={t} revision={revision} columns={["License", "Plan", "Linked", "Expiry", "Status", "Actions"]}
        sorts={[["created", "Created"], ["label", "Label"], ["expiry", "Expiry"]]} render={row => {
          const status = row.revoked ? "Revoked" : !row.expires_at ? "First activation" : Number(row.expires_at) <= Date.now() / 1000 ? "Expired" : "Active";
          const revoke: Action = {
            title: row.revoked ? "Restore" : "Revoke", danger: !row.revoked, path: "licenses/" + row.id + "/revoke", method: "POST",
            description: row.revoked ? "Restore this license? Its linked devices can verify again." : "Revoke this license? Its devices will lose access at the next verification.",
            body: () => ({ revoked: !row.revoked })
          };
          return <tr key={String(row.id)}><td><strong>{row.label}</strong><small className="mono">{row.id}</small></td>
            <td>{row.plan_id ?? "—"}</td><td>{row.used} / {row.device_limit}</td><td>{timestamp(row.expires_at, "First activation")}</td>
            <td><span className={"pill " + (status === "Active" ? "positive" : status === "First activation" ? "" : "warn")}>{t(status)}</span></td>
            <td className="row-actions">
              <button onClick={() => setDeviceFilter(String(row.id))}>{t("Devices")}</button>
              {owner && <>
                {button("Edit", licenseAction(row))}
                {button("Extend", { title: "Extend", method: "POST", path: "licenses/" + row.id + "/extend",
                  fields: [numberField("days", "Additional days", 30, 1, 3650)], body: data => ({ days: Number(data.get("days")) }) })}
                {button(revoke.title, revoke)}
              </>}
            </td></tr>;
        }} />
    </>;
  } else if (selected[0] === "devices") {
    content = <Listing path="devices" t={t} revision={revision} columns={["Device", "License", "Activated", "Last seen", ...(owner ? ["Actions"] : [])]}
      sorts={[["seen", "Last seen"], ["activated", "Activated"], ["label", "Label"]]} render={row => deviceRow(row)} />;
  } else if (selected[0] === "plans") {
    content = <>
      <div className="subhead"><h2>{t("Plans")}</h2>{owner && button("Save plan", planAction(), "primary")}</div>
      <Listing path="plans" t={t} revision={revision} columns={["Plan", "Limit", "Status", ...(owner ? ["Actions"] : [])]}
        sorts={[["name", "Name"], ["limit", "Limit"]]} render={row => <tr key={String(row.id)}><td><strong>{row.name}</strong><small className="mono">{row.id}</small></td>
          <td>{row.max_devices}</td><td>{t(row.status === "active" ? "Active" : "Archived")}</td>
          {owner && <td>{button("Edit", planAction(row))}</td>}</tr>} />
      <div className="subhead"><h2>{t("Features")}</h2>{owner && button("Set feature", featureAction(), "primary")}</div>
      <Listing path="features" t={t} revision={revision} columns={["Feature", "Scope", "Scope ID", "Value", ...(owner ? ["Actions"] : [])]}
        sorts={[["key", "Feature"], ["scope", "Scope"]]} render={row => <tr key={[row.feature_key, row.scope_type, row.scope_id].join("/")}>
          <td>{t(featureOptions.find(([key]) => key === row.feature_key)?.[1] ?? String(row.feature_key))}</td>
          <td>{t(row.scope_type === "global" ? "Global" : row.scope_type === "plan_default" ? "Plan default" : row.scope_type === "plan" ? "Plan" : "License")}</td>
          <td className="mono">{row.scope_id || "—"}</td><td>{t(row.value_json === "true" ? "Enabled" : "Disabled")}</td>
          {owner && <td>{button("Edit", featureAction(row))}</td>}</tr>} />
    </>;
  } else if (selected[0] === "releases") {
    content = <>
      {owner && <div className="section-actions">{button("Register release", releaseAction, "primary")}</div>}
      <Listing path="releases" t={t} revision={revision} columns={["Version", "Channel", "Status", "Installer SHA-256", "Created"]}
        sorts={[["created", "Created"], ["channel", "Channel"]]} render={row => <tr key={String(row.id)}>
          <td><a href={String(row.manifest_url)} target="_blank" rel="noreferrer">{row.version}</a></td>
          <td>{t(row.channel === "stable" ? "Stable" : "Test")}</td><td>{t(row.status === "ready" ? "Ready" : "Withdrawn")}</td>
          <td className="mono" title={String(row.checksum)}>{String(row.checksum).slice(0, 16)}…</td><td>{timestamp(row.created_at)}</td></tr>} />
    </>;
  } else if (selected[0] === "audit") {
    content = <Listing path="audit" t={t} revision={revision} columns={["Time", "Actor", "Action", "Entity", "Details"]}
      sorts={[["time", "Time"], ["action", "Action"]]} render={row => <tr key={String(row.id)}>
        <td>{timestamp(row.occurred_at)}</td><td>{row.actor}</td><td>{row.action}</td>
        <td>{row.entity_type}<small className="mono">{row.entity_id}</small></td><td className="mono metadata">{row.metadata_json}</td></tr>} />;
  } else if (selected[0] === "system") {
    content = <SystemPanel revision={revision} t={t} edit={setAction} />;
  } else if (resource.value) {
    content = <>
      <div className="metrics">
        {([["Licenses", resource.value.licenses], ["Available", resource.value.available], ["Devices", resource.value.devices],
          ["Recently active", resource.value.recentlyActive, "Verified in the last 15 minutes"]] as const).map(([label, value, hint]) =>
          <article className="metric" key={label}><span>{t(label)}</span><strong>{value.toLocaleString(language === "ar" ? "ar-EG" : "en")}</strong>{hint && <small>{t(hint)}</small>}</article>)}
      </div>
      <section className="panel"><div className="toolbar"><h2>{t("Releases")}</h2></div><ChannelTable items={resource.value.channels} t={t} /></section>
    </>;
  }

  function deviceRow(row: Row) {
    return <tr key={String(row.license_id) + "/" + String(row.device)}>
      <td><span className="mono" title={String(row.device)}>{String(row.device).slice(0, 16)}…</span></td>
      <td>{row.label}<small className="mono">{row.license_id}</small></td><td>{timestamp(row.activated_at)}</td><td>{timestamp(row.last_seen)}</td>
      {owner && <td>{button("Unlink", { title: "Unlink", description: "Unlink this device? It will lose access at the next verification.",
        method: "DELETE", path: "licenses/" + row.license_id + "/devices/" + row.device, danger: true })}</td>}</tr>;
  }

  return <div className="shell">
    <aside className="sidebar">
      <div className="brand">
        <div><strong>SpectraVision</strong><small>{t("Cloud Control")}</small></div>
      </div>
      {actor && <nav aria-label={t("Cloud Control")}>{navigation.filter(([key]) => owner || !["audit", "system"].includes(key)).map(([key, label]) =>
        <a href={"#" + key} key={key} aria-current={key === selected[0] ? "page" : undefined}>{t(label)}</a>)}</nav>}
      {actor && <div className="identity"><small>{t(roles[actor.role])}</small><span title={actor.email}>{actor.email}</span></div>}
    </aside>
    <main>
      <header><h1>{t(selected[1])}</h1><div className="preferences">
        <button onClick={() => setLanguage(language === "ar" ? "en" : "ar")}>{language === "ar" ? "English" : "العربية"}</button>
        <button onClick={() => setLight(!light)}>{t(light ? "Dark mode" : "Light mode")}</button>
      </div></header>
      {!actor ? <Feedback resource={resource} t={t} /> : <div key={selected[0]} className="content">{content}</div>}
    </main>
    {deviceFilter && <DeviceDialog id={deviceFilter} revision={revision} t={t} owner={owner}
      close={() => setDeviceFilter("")} render={deviceRow} />}
    {action && <ActionDialog key={action.path + action.method} action={action} t={t} close={() => setAction(undefined)} done={created => {
      setAction(undefined); setRevision(value => value + 1);
      if (created) setCode(created); else setToast("Saved");
    }} />}
    {code && <Modal title="New license" close={() => setCode("")} t={t}>
      <p>{t("Copy the activation code now. It is displayed once.")}</p><output className="activation-code" dir="ltr">{code}</output>
      <div className="dialog-actions"><button className="primary" onClick={async () => {
        try { await navigator.clipboard.writeText(code); setToast("Copied"); }
        catch { setToast("Request failed"); }
      }}>{t("Copy")}</button><button onClick={() => setCode("")}>{t("Close")}</button></div>
    </Modal>}
    {toast && <div className="toast" role="status">{t(toast)}</div>}
  </div>;
}

function DeviceDialog({ id, revision, t, close, owner, render }: {
  id: string; revision: number; t: Translate; close: () => void; owner: boolean; render: (row: Row) => ReactNode;
}) {
  const resource = useResource<Row[]>("licenses/" + id + "/devices", revision);
  return <Modal title="Devices" close={close} t={t}>
    <Feedback resource={resource} t={t} />
    {resource.value && (resource.value.length ?
      <div className="table-wrap"><table><thead><tr>{["Device", "License", "Activated", "Last seen", ...(owner ? ["Actions"] : [])].map(label =>
        <th scope="col" key={label}>{t(label)}</th>)}</tr></thead><tbody>{resource.value.map(render)}</tbody></table></div>
      : <p>{t("License has no linked devices")}</p>)}
  </Modal>;
}

createRoot(document.getElementById("root")!).render(<App />);
