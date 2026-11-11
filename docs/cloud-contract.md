# عقد SpectraVision Cloud Control v2

يخدم Client Worker مسارات `/v2/client/` على HTTPS. يحدد الـWorker قناة الإصدار حسب البيئة. الإنتاج على `stable` وStaging على `test`. يظل عقد v1 في `licensing/worker.mjs` مع عنوانه الحالي خلال الانتقال.

## عميل Windows

الحقول الزمنية هي ثوانٍ منذ Unix epoch. قيمة `device` بصمة من 64 خانة hex. حقل `version` ثلاثة أرقام SemVer من دون suffix.

| الطلب | الحقول والرد |
| --- | --- |
| `POST /v2/client/activate` | `device` و`version` و`code`. لترحيل جهاز v1 استخدم `legacyToken` مكان `code`. النجاح يرجع تفويضًا و`token` جديدًا. |
| `POST /v2/client/heartbeat` | `device` و`version` و`token`. النجاح يجدد التفويض. |
| `POST /v2/client/deactivate` | `device` و`version` و`token`. ينهي الجلسة ويرجع `{ "ok": true }` عند التكرار. المقعد يظل مربوطًا حتى يفكه المسؤول. |
| `GET /v2/client/version` | `channel` و`currentVersion` و`minSupportedVersion` و`maintenance` و`manifestUrl` و`checksum`. حقول الإصدار الحالي تكون `null` قبل ترقية القناة. |
| `GET /v2/client/health` | `{ "ok": true }`. استخدم فحص الإصدار أيضًا للتحقق من ربط D1. |

التفويض الناجح يحمل `authorized: true` و`serverTime` و`leaseExpiresAt` و`expiresAt` و`permissions` للمفاتيح `demo` و`live` و`inspection`. مدة التصريح بحد أقصى 128 ثانية. يستهدف العميل تجديده كل 120 ثانية بمهلة طلب 8 ثوانٍ. يأخذ العميل مدة التصريح من فرق وقتي السيرفر ويقيسها بـStopwatch من بداية الطلب. إذا فشل التحقق، تتوقف الجلسة المحمية ويبدأ المستخدم جلسة جديدة بعد استعادة التفويض.

يرجع الفشل `{ "error": "..." }`.

| الحالة | الأكواد |
| --- | --- |
| 400 / 413 / 415 | `bad_input` للمدخلات غير الصالحة أو أكبر من 4096 بايت أو من نوع محتوى خاطئ |
| 403 | `invalid_code`، `expired`، `revoked`، `device_limit`، `device_removed` |
| 426 | `update_required` |
| 429 | `rate_limited` |
| 503 | `maintenance` أو `service_unavailable` |

التفعيل يعيد استخدام نفس المقعد على الجهاز ويدوّر جلسة v2. ترحيل رمز v1 يترك ربط v1 وبصمته متاحين للرجوع. فك الجهاز يلغي الجلسات المرتبطة به. حد الأجهزة الفعلي هو الأقل بين حد الترخيص وحد الباقة. تعيين باقة أو خفض حدها يحفظ السقف في حقل v1 أيضًا. تعديل سقف الباقة للأعلى يسمح بتعديل حدود التراخيص لاحقًا.

## الإدارة

تبدأ المسارات بـ`/v2/admin/`. تتطلب طلبات التغيير Origin مطابقًا لعنوان لوحة الإدارة. عمليات الكتابة لها سجل تدقيق في نفس معاملة D1.

| المورد | العمليات |
| --- | --- |
| `overview` | GET لحساب الهوية والأعداد والقنوات |
| `licenses` | GET لقائمة التراخيص وPOST بالحقول `label` و`limit` و`days` أو `expiresAt`، مع `plan` اختياريًا |
| `licenses/:id` | GET وPATCH بنفس الحقول الاختيارية. يمكن تغيير مدة الأيام قبل أول تفعيل. |
| `licenses/:id/revoke` | POST بمتغير `revoked`. القيمة الافتراضية `true`. |
| `licenses/:id/extend` | POST بعدد `days`. التراخيص المفعلة تمدد من أقصى تاريخ بين وقت الطلب وتاريخ الانتهاء الحالي. |
| `licenses/:id/devices` | GET لأجهزة الترخيص |
| `licenses/:id/devices/:deviceId` | GET للجهاز وDELETE لفك الربط |
| `devices` | GET لقائمة الأجهزة |
| `plans` | GET وPUT بالحقول `id` و`name` و`status` و`maxDevices` |
| `features` | GET وPUT بالحقول `key` و`scope` و`id` و`enabled`. قيمة `null` تحذف تجاوز الإعداد. |
| `releases` | GET وPOST بالحقول `channel` و`version` و`manifestUrl` و`checksum` |
| `system` | GET وPUT بحقل `maintenance` عامًا، أو `channel` مع `currentVersion` و`minSupportedVersion` و`maintenance` |
| `audit` | GET لسجل التدقيق |

القوائم تدعم `search` و`offset` و`limit` و`sort` و`order`. شكل الرد `{ items, nextOffset }`. اختيار `sort` محدود بأعمدة كل مورد. الدور `support` يقرأ الموارد التشغيلية فقط. الدور `owner` يدير الموارد ويقرأ التدقيق وإعدادات النظام. `Recently active` يقيس التحقق خلال آخر 15 دقيقة، مع تخزين آخر ظهور مرة كل 10 دقائق أثناء Heartbeat.

ترتيب أولوية المزايا من الأقل للأعلى هو القيمة الافتراضية المدمجة، ثم `global`، ثم `plan_default`، ثم تجاوز `plan`، ثم `license`. إصدار المثبّت يبقى منفصلًا عن اختيار الإصدار الحالي للقناة.
