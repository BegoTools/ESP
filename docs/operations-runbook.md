# تشغيل SpectraVision Cloud Control

## البيئات

| البيئة | Client Worker | Admin Worker | D1 |
| --- | --- | --- | --- |
| Staging | `sv-client-api-v2-staging` | `sv-admin-v2-staging` | `spectravision-licenses-staging` |
| الإنتاج | `sv-client-api-v2` | `sv-admin-v2` | `spectravision-licenses` |

من Workers & Pages افتح Worker الإدارة ثم Access واختر حماية All traffic. اسمح بحساب المالك وحسابات الدعم المعتمدة فقط. طبق ذلك لكل بيئة على حدة. في أول إعداد للإنتاج، أنشئ Worker الإدارة بالاسم المحدد باستخدام قالب فارغ من بيانات التطبيق، ثم فعّل Access قبل تشغيل workflow الإنتاج. يتحقق workflow من التحويل إلى صفحة Access قبل بدء ترحيل D1. اترك Client Worker متاحًا لبرنامج Windows. [إعداد Cloudflare Access للـWorkers](https://developers.cloudflare.com/workers/configuration/cloudflare-access/)

في Worker الإدارة خزّن `ACCESS_ISSUER` بعنوان الفريق مثل `https://TEAM.cloudflareaccess.com` من دون slash أخير. خزّن `ACCESS_AUD` بمعرّف Application Audience. خزّن `ADMIN_ROLES` كـJSON مفاتيحه عناوين بريد بالحروف الصغيرة وقيمه `owner` أو `support`. كرر بقيم تخص كل بيئة. استخدم لوحة Cloudflare أو `wrangler secret put` مع ملف إعدادات البيئة. يتطلب السماح بالوصول نجاح السياسة ووجود الدور في القائمة. راجع الوصول بحساب فعلي وجرّب الرفض من جلسة غير مصرح بها.

بيئة GitHub المسماة `staging` تستقبل النشر من `develop`. بيئة `production` محصورة في `main` وتتطلب مراجعة يدوية. استخدم رمز Cloudflare محدودًا بحسابك لإدارة Workers Scripts وD1، مع صلاحيات القراءة اللازمة لجرد الحساب. ضع كل رمز في بيئته باسم `CLOUDFLARE_API_TOKEN` واضبط `CLOUDFLARE_ACCOUNT_ID` كمتغير. سر `SV_RELEASE_SIGNING_KEY` يجب أن يطابق مفتاح القناة. مفتاح الاختبار منفصل عن مفتاح الإنتاج. [GitHub Environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)

## التحقق

```powershell
cd licensing
npm ci
npm run check
npm run build
npm test
npx --no-install wrangler d1 migrations apply spectravision-licenses-staging --local --config wrangler.staging.client.jsonc
node tests/smoke.mjs staging --require-access
node tests/staging-load.mjs
```

اختبار الحمل يتطلب صلاحية D1 في حساب Staging ويزرع بيانات وهمية مؤقتًا. نفّذه من عنوان IP واحد بلا عمليات تفعيل كثيفة بالتوازي. لتجربة Windows، شغّل `installer/tests/Test-OnlineLicensing.ps1` ثم ابنِ مثبّت Staging بالأمر `./installer/build-release.ps1 -Version 2.0.0 -Test`. البناء `-Test -Local` يتصل بـlocalhost ويستخدم اسم منتج مختلفًا.

## مراقبة الحصة والحالات

- احتسب 720 Heartbeat يوميًا لكل جهاز يعمل 24 ساعة. 50 جهازًا يولّد 36,000 طلبًا تقريبًا. كل Heartbeat ناجح قد يحدث قراءة إعدادات القناة وبيانات الجلسة والمزايا. أول ظهور أو كل 10 دقائق يمكن أن يحدث كتابة على الجهاز والفهرس.
- راجع لوحة الحصة لكل الحساب، بما في ذلك v1 وStaging ولوحة الإدارة. الأصول في Admin Worker تمر عبر Worker بسبب المصادقة. اضبط التنبيهات أو المراجعات التشغيلية عند 70% و90% من الحد الفعلي. [Workers limits](https://developers.cloudflare.com/workers/platform/limits/) و[D1 pricing](https://developers.cloudflare.com/d1/platform/pricing/)
- راقب 403 و429 و503 ومعدلات فشل التحديث. جرّب `/v2/client/health` ثم `/v2/client/version` لتمييز استجابة Worker عن توفر D1. ربط العميل يفشل بأمان عند تعذر D1 أو التحقق.
- استخدم الصيانة العامة أو صيانة قناة محددة من Settings عند توقف مخطط له. يظهر سبب الصيانة للعميل عند التحقق التالي.

## بوابة الإطلاق

- [ ] التحقق من Cloudflare Access على HTML وAdmin API بحساب مالك ومستخدم غير مصرح.
- [ ] التحقق من نشر GitHub Actions عبر staging باستخدام رمز Cloudflare الخاص بها.
- [ ] تثبيت وترقية نظيفان على Windows وتجربة Demo وLive وحالات فصل الشبكة واستئناف الجهاز مع جلسة فعلية.
- [ ] التحقق من تنزيل تحديث GitHub Release موقّع وتجربة الرجوع.
- [ ] مراجعة أرقام Workers وD1 أثناء الاستخدام الفعلي وتوثيق نقطة الرجوع.
- [ ] تجربة الجسر مع دفعة محدودة ومراجعة تشغيلها 24–48 ساعة.
- [ ] موافقة الإنتاج بعد قبول النتائج.
