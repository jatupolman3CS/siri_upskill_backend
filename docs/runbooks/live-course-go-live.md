# Runbook — เปิดใช้ระบบคอร์สสอนสด (Live / Google Meet) บน production

> เขียนหลัง integrator-qa gate 2026-10-09 ที่เดินระบบทั้งเส้นทางบนเครื่อง dev ด้วย topology เดียวกับ production (**API process เดียว ไม่มี Workers, DB ว่าง**) — รายละเอียดผลตรวจอยู่ท้ายไฟล์ · ไม่มี secret ในไฟล์นี้ (ค่าจริงอยู่ใน env/secret ของ cluster เท่านั้น)

## 1) production พังตรงไหน และตอนนี้แก้อะไรไว้แล้ว

| # | อาการเดิมบน production | สาเหตุจริง | แก้ใน code (ต้อง deploy image ใหม่ถึงมีผล) |
|---|---|---|---|
| 1 | ไม่มีอีเมลเลย (ยืนยันอีเมล/คำเชิญ/เตือน), ห้องไม่ถูกตัดสิน | มี **Hangfire job server เฉพาะใน `Siri.Workers`** แต่ pod production รันแค่ `Siri.Api` → ไม่มี job ใดรันเลย | `Hangfire__ServerInApi` (default **true**): API รัน job server + ลงทะเบียน recurring job เอง · Workers แยกยังรันคู่กันได้ปลอดภัย |
| 2 | คาบที่สร้างใหม่ค้างสถานะ "รอ" จน job ทำงาน | provider ของห้องถูกตัดสินโดย job เท่านั้น | ตัดสิน **ทันทีตอนสร้างคาบ**: ยังไม่เชื่อ Google → `AwaitingLink`/`PasteLink` ในคำขอเดียวกัน |
| 3 | DB ว่าง ผู้สอนสร้างคอร์สไม่ได้ (ต้องเลือกหมวดหมู่) และไม่มีที่อนุมัติผู้สอน | ไม่มีหน้า admin | เพิ่ม `/admin/categories`, `/admin/instructors`, `/admin/live-status` (+ `GET /api/live/admin/status`) |
| 4 | **ไม่มีทางมี Admin คนแรกใน DB ว่าง** | `OwnerAccountBootstrapper` ถูกเขียนไว้แต่ **ไม่เคยถูก register หรือเรียกที่ไหนเลย** (dead code) | **แก้ใน gate นี้:** `OwnerBootstrapService` (`src/Siri.Api/Bootstrap/`) ผูกเข้า `Program.cs` — ตั้ง `Identity__Bootstrap__OwnerEmails__0` แล้วบัญชีนั้นได้ครบทุก role + โปรไฟล์ผู้สอนที่อนุมัติแล้วเอง ภายใน ~30 วินาทีหลังสมัคร+ยืนยันอีเมล **ไม่ต้อง restart** |
| 5 | ผู้สอนกด "เผยแพร่คอร์ส" ของคอร์ส Live ที่มีแต่คาบสอน (ไม่มีวิดีโอ) ไม่ได้ ขึ้น "กรุณาอัปโหลดวิดีโอ…" | FE `mediaReadyForReview` บังคับต้องมีบทเรียนวิดีโอทุกคอร์ส ทั้งที่ backend อนุญาต Live/Hybrid ที่มีคาบอนาคต | **แก้ใน gate นี้:** `course-builder-page.ts` รู้ format ของคอร์ส (live tab รายงาน / ถามจาก `live-schedule` ตอนกดส่ง) — Live/Hybrid ไม่มีบทเรียนก็ส่งตรวจได้ (server ยังเป็นคนตัดสินว่าห้องครบ) · OnDemand ยังต้องมีวิดีโอเหมือนเดิม |
| 6 | ผู้เรียนเปิด `/learn/<คอร์ส Live>` เห็นจอดำ + spinner "กำลังเตรียมเครื่องเล่นวิดีโอ…" ไม่จบ | คอร์สไม่มีบทเรียนให้เล่น | **แก้ใน gate นี้:** แสดงข้อความ "คอร์สนี้สอนสด ยังไม่มีบทเรียนวิดีโอ" + ปุ่มไปแท็บตารางสอนสด |

## 2) ลำดับ deploy (ทำตามนี้ทีละข้อ)

1. **migration ก่อน image เสมอ** — `dotnet ef migrations list --project src/Siri.Persistence --startup-project src/Siri.Api` (หรือ bundle `scripts/migrate-bundle.sh`) · ตอนเขียนไฟล์นี้ working tree มี `20261008201343_AddNotificationKafkaDelivery` (เพิ่ม `NOTIFY.EMAIL_OUTBOX.QUEUED_AT_UTC`, `NOTIFY.NOTIFICATIONS.PUBLISHED_AT_UTC` + index) สร้าง **หลัง** การตรวจ "0 pending" ของ 2026-10-08 → น่าจะยัง pending · **ถ้า image ใหม่ขึ้นก่อน migration นี้ อีเมล/แจ้งเตือนจะ error 500 เพราะคอลัมน์ไม่มี** · ห้าม apply ด้วยมือบน DB จริงโดยไม่รู้ตัว — เป็นคำสั่งของเจ้าของระบบ
   · **เพิ่ม (P11-13):** `AddLiveRecordingImport` (ตาราง `LIVE.SESSION_RECORDING_IMPORTS` + 2 คอลัมน์บน `LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS`, additive ล้วน) ต้อง apply ก่อน image ใหม่เช่นกัน — รายละเอียดหัวข้อ 9
2. ตั้ง env ของ **API pod** ตามหัวข้อ 3 (ไม่มีค่าไหนใน repo)
3. build + push image `Dockerfile.api` (+ image frontend ของ repo `siri_upskill_ui`) แล้ว rollout
4. รอ ~2 นาที → เปิด `/admin/live-status` (หัวข้อ 4) · หรือค้น log pod ด้วยคำว่า `Live system check` (log 1 บรรทัดที่ ~90 วินาทีหลัง start: `no warnings` หรือ Warning พร้อมรหัส)
5. (ถ้ามี pod Workers เดิมอยู่) ปล่อยไว้ได้ — ซ้ำกับ API server ไม่เกิดงานซ้ำ (recurring job id เดียวกัน + lock ใน storage) · Workers ต้องได้ `DataProtection__EncryptionKeyBase64` **ตัวจริงเดียวกับ API** ไม่งั้นถอดรหัส token Google ของผู้สอนไม่ได้

## 3) Environment ที่ API pod ต้องมี

**บังคับ (ไม่ครบ = pod boot ไม่ขึ้น — `ProductionConfigurationGuard` fail-fast และบอกชื่อที่ขาด):**

| env | ความหมาย |
|---|---|
| `ASPNETCORE_ENVIRONMENT=Production` | |
| `ConnectionStrings__Default` | Npgsql (host จริงของ cluster — ห้ามชี้ `127.0.0.1:5433` ของเครื่อง dev) |
| `Redis__ConnectionString` | ต้องเชื่อมต่อได้จริง (ไม่ใช่แค่ไม่ว่าง) — Redis ใช้กับ session mirror, cache, state ของ OAuth Google · ถ้า Redis ล่ม เว็บยังทำงานได้ (ส่วนใหญ่ fail-open) และปุ่ม "เชื่อมต่อ Google Calendar" ยังใช้ได้โดยเก็บ state ไว้ในหน่วยความจำของ API pod (**ใช้ได้เฉพาะเมื่อมี API pod เดียว** · มี warning `Google OAuth state: Redis is not connected` ใน log ทุกครั้ง) · scale เป็นหลาย pod ต้องแก้ Redis ก่อน |
| `Identity__Jwt__SigningKey` | ≥ 32 ตัวอักษร ไม่มีคำว่า CHANGE_ME |
| `DataProtection__EncryptionKeyBase64` | 32 ไบต์ base64 (`openssl rand -base64 32`) — **เก็บให้ดี เปลี่ยนแล้วข้อมูลเข้ารหัสเดิม (เลขบัญชี, token Google) ถอดไม่ได้** |
| `Cors__AllowedOrigins__0` | origin เว็บจริง (https, ห้าม localhost/`*`) |
| `Seo__PublicBaseUrl` | `https://…` ของเว็บ |
| `Live__PublicBaseUrl` | `https://…` (ถ้าไม่ตั้งใช้ `Seo__PublicBaseUrl`) — ใช้สร้างลิงก์ `/live/{sid}/join` ในอีเมล/ICS |
| `Email__Provider=Smtp`, `Email__Smtp__Host`, `__Port`, `__Username`, `__Password`, `__FromAddress` (โดเมนจริง), `__FromDisplayName`, `__UseStartTls=true` | `AllowInsecure` ต้อง false |
| `Payment__Stripe__SecretKey` (`sk_live_`), `__PublishableKey` (`pk_live_`), `__WebhookSecret` (`whsec_`) | webhook ชี้ `POST https://<api>/api/commerce/webhooks/stripe` · events: `payment_intent.succeeded`, `payment_intent.payment_failed`, `payment_intent.canceled` |
| `VideoProvider__LibraryId`, `__ApiKey`, `__ReadOnlyApiKey`, `__PullZone`, `__CdnHostname`, **`__TokenAuthenticationKey`** | Bunny — ครบทุกตัว · webhook Bunny: `POST https://<api>/api/media/webhooks/bunny` |
| `Payout__PayerCompanyName`, `__PayerTaxId` (13 หลัก), `__PayerAddress` | นิติบุคคลจริง (ใช้เป็นผู้ขายบนใบเสร็จด้วย ถ้าไม่ตั้ง `Commerce__Seller__*`) |

**สำหรับระบบ Live (ตั้งแล้วระบบทำงานครบ):**

| env | ค่า | หมายเหตุ |
|---|---|---|
| `Identity__Bootstrap__OwnerEmails__0` | อีเมลเจ้าของระบบ (ใส่ `__1`, `__2` เพิ่มได้) | **ไม่ใช่ secret** · บัญชีต้องสมัครเอง + ยืนยันอีเมลก่อน (Active) ถึงได้สิทธิ์ · ได้ role ครบ + audit `OwnerBootstrapRolesGranted` · role มีผลตอน **login ครั้งถัดไป** |
| `Hangfire__ServerInApi` | ไม่ต้องตั้ง (default `true`) | ตั้ง `false` เฉพาะเมื่อมี Workers แยกและต้องการให้ API ไม่รันงาน |
| `Live__Provider` | ไม่ต้องตั้ง (default `GoogleMeet`) | ยังไม่มี Google credential → ระบบตกเป็น "ผู้สอนวางลิงก์เอง" อัตโนมัติ (ใช้งานได้เต็มที่ แค่ `/admin/live-status` จะเตือน `google_not_configured`) · `ManualOnly` = ไม่เรียก Google เลย · **ห้าม `Logging`** (production ไม่ยอมบูต) |
| `Integrations__Google__ClientId` / `__ClientSecret` / `__RedirectUri` | จาก Google Cloud (ข้างล่าง) | ตั้ง ClientId แล้วต้องมี Secret + RedirectUri `https://` ครบ ไม่งั้นบูตไม่ขึ้น · ตั้งให้ **API และ Workers ทั้งคู่** ถ้ามี Workers |

**Google Cloud (ทำเมื่อพร้อมเปิดสร้าง Meet อัตโนมัติ — ไม่บล็อกการใช้งานตอนนี้):**
1. สร้าง/เลือก project → **Enable "Google Calendar API"**
2. OAuth consent screen: ใส่ชื่อแอป/อีเมลติดต่อ/โดเมน, scopes `openid`, `email`, `https://www.googleapis.com/auth/calendar.events.owned` (เป็น sensitive scope → ต้องส่งตรวจสอบแอปก่อนเปิดให้ผู้ใช้ทั่วไป) · ระหว่างสถานะ **Testing** ต้องเพิ่มอีเมลผู้สอนใน **Test users** ทุกคน และ (ตามนโยบาย Google) refresh token ของแอปสถานะ Testing หมดอายุเร็ว (~7 วัน) → ผู้สอนต้องกดเชื่อมใหม่ — ตรวจเงื่อนไขล่าสุดใน console
3. Credentials → OAuth client ID ชนิด **Web application** → Authorized redirect URI = ค่า `Integrations__Google__RedirectUri` **ตรงทุกตัวอักษร**: `https://<api origin>/api/live/instructor/google/callback`
4. เอา Client ID/Secret ใส่ env แล้ว rollout → `/admin/live-status` ต้องเลิกเตือน `google_not_configured` → ผู้สอนเข้า `/instructor/live-settings` กด "เชื่อม Google Calendar"
5. ผล: คาบใหม่ของผู้สอนที่เชื่อมแล้วจะสร้าง event + Meet ให้เอง (job `live-meeting-sync` ทุกนาที) · ผู้ไม่ได้เชื่อมยังวางลิงก์เองได้เหมือนเดิม

## 4) เช็คครั้งแรกบนเบราว์เซอร์ (เจ้าของระบบ ~15 นาที)

1. **สมัครบัญชีเจ้าของ** ด้วยอีเมลใน `Identity__Bootstrap__OwnerEmails__0` → กดลิงก์ยืนยันในอีเมล (ถ้าอีเมลไม่มา = SMTP/job server ยังไม่ทำงาน ดูหัวข้อ 6) → **รอ ≤ 1 นาที แล้ว logout/login ใหม่** → avatar menu ต้องมี "ผู้ดูแลระบบ" และ "สตูดิโอผู้สอน"
2. **`/admin/live-status`** ต้องเห็น: Hangfire "ทำงานอยู่" ชื่อเซิร์ฟเวอร์ขึ้นต้น `api:` · งานประจำ 6 ตัว (`email-outbox-send`, `live-meeting-sync`, `live-invite-reconcile`, `live-session-reminders`, `live-recording-import`, `course-enrollment-recount`) มี "รันล่าสุด" ใหม่ ๆ · อีเมล = SMTP · คำเตือนที่ยอมรับได้มีแค่ `google_not_configured` (จนกว่าจะตั้ง Google) — อย่างอื่นให้ดูตารางหัวข้อ 5
3. **`/admin/categories`** → สร้างหมวดหมู่หลัก (+ย่อย) อย่างน้อย 1 รายการ (ไม่มีหมวด = ผู้สอนสร้างคอร์สไม่ได้)
4. ให้ผู้สอนสมัคร → `/become-instructor` → เจ้าของไป **`/admin/instructors`** กดอนุมัติ → ผู้สอน login ใหม่ (role มีผลตอน login) · บัญชีรับเงินไม่อยู่ในฟอร์มสมัครแล้ว: ผู้สอนที่อนุมัติแล้วเพิ่มได้ที่ `/instructor/earnings` (แก้ข้อมูลบัญชี = แอดมินต้องตรวจสอบใหม่ก่อนโอน)
5. ผู้สอน: `/instructor/courses/new` → กรอกชื่อ/หมวด/ราคา → "บันทึกร่าง" → แท็บ **ตารางสอนสด** → รูปแบบ "สอนสด" → "เพิ่มคาบ" (≥1 คาบในอนาคต) → แต่ละคาบต้องขึ้น "รอลิงก์ห้อง" **ทันที** → กด "สร้างห้อง Google Meet" (เปิดแท็บ meet.google.com/new) → คัดลอกลิงก์มาวาง "บันทึกลิงก์" → ครบทุกคาบ → "เผยแพร่คอร์ส" (ถ้ามีคาบไหนไม่มีห้อง ระบบปฏิเสธและทำเครื่องหมายคาบนั้น)
6. เจ้าของ `/admin/courses` → "อนุมัติเผยแพร่" → หน้า `/courses/<slug>` ต้องเห็นตารางสอนสด **โดยไม่มีลิงก์ห้อง**
7. **ซื้อทดสอบ**: สร้างโค้ดส่วนลด 100% (admin → การตลาด/โค้ด) ซื้อด้วยบัญชีผู้เรียน → ภายใน ~2 นาทีต้องได้อีเมล "ยืนยันตารางเรียนสด" + ไฟล์ .ics (เฉพาะคาบอนาคต, ลิงก์เป็น `…/live/{sid}/join` เท่านั้น) · ก่อนเริ่ม 15 นาทีผู้เรียนกด "เข้าห้องเรียน" ที่ `/my-courses` หรือ `/learn/<slug>?tab=live` → ไปห้อง Meet · จ่ายจริงด้วย PromptPay ก็ทำได้ แต่ **ยังไม่เคยทดสอบ webhook จนจบ** (ดูข้อจำกัด)

## 5) อ่าน `/admin/live-status` และ log

หน้านี้รีเฟรชเองทุก 30 วินาที · log บรรทัด `Live system check` ใช้รหัสเดียวกัน (เฉพาะ Production, ~90 วินาทีหลัง start)

| รหัส | แปลว่า | ทำอะไร |
|---|---|---|
| `no_job_server` | ไม่มี Hangfire server heartbeat สด → ไม่มีงานใดรัน | ตรวจว่า `Hangfire__ServerInApi` ไม่ใช่ `false` (ถ้าใช่ต้องมี Workers) · ดู log pod · เซิร์ฟเวอร์ที่ตายแล้วค้างในลิสต์ได้ ~5 นาที (ปกติ) |
| `job_storage_unreadable` | อ่านตาราง `hangfire.*` ไม่ได้ | ตรวจ DB/สิทธิ์ schema `hangfire` |
| `recurring_jobs_missing` | ยังไม่มี host ลงทะเบียน recurring job | รอ 1 นาทีหลัง start · ถ้ายังไม่ขึ้นดู log `Recurring background jobs registered` |
| `recurring_job_overdue` / `recurring_job_failing` | งานเลยเวลา / ครั้งล่าสุดล้ม | ดู exception ที่ `/hangfire` (เข้าได้เฉพาะ localhost เช่น `kubectl port-forward` ไปที่ pod) หรือใน log pod |
| `email_unconfigured` | `Email__Provider` ไม่ใช่ `Smtp` | ตั้ง SMTP ตามหัวข้อ 3 |
| `outbox_backlog` / `outbox_failures` | อีเมลค้าง/ส่งไม่ออกครบ retry | ตรวจ SMTP host/port/user/pass/StartTLS/From domain |
| `public_base_url_not_https` | `Live__PublicBaseUrl`/`Seo__PublicBaseUrl` ไม่ใช่ https | ตั้งเป็น https จริง (localhost จะเตือนตลอด — ปกติบน dev) |
| `google_not_configured` | provider เป็น GoogleMeet แต่ไม่มี Google client | ปกติถ้ายังไม่ตั้ง Google (ผู้สอนวางลิงก์เอง) |
| `live_provider_logging` | provider ปลอมสำหรับ dev | production ไม่ยอมบูต — ไม่ควรเห็น |
| `meetings_stuck_pending` / `meetings_failed` | ห้องค้างเกินรอบ job / สร้างไม่สำเร็จหลัง retry | ผู้สอนกด "ลองสร้างห้องใหม่" หรือวางลิงก์เอง · เช็ค job server |
| `email_status_unreadable` / `live_status_unreadable` | อ่านตัวเลขไม่ได้ (ตาราง/สิทธิ์) | เช็ค migration ว่าครบ |

## 6) แก้ปัญหาตามอาการ

| อาการ | ตรวจ | แก้ |
|---|---|---|
| สมัครแล้วไม่มีอีเมลยืนยัน | `/admin/live-status` (ยังเข้าไม่ได้ถ้ายังไม่เป็น admin → ดู log `Live system check`) · log `Email` | job server ไม่รัน หรือ SMTP ผิด (ตาราง 5) · อีเมลไม่หายเพราะค้างใน `NOTIFY.EMAIL_OUTBOX` และ retry เอง |
| login เจ้าของแล้วไม่เห็น "ผู้ดูแลระบบ" | log `Owner bootstrap:` · สถานะบัญชี | อีเมลไม่ตรงค่า env / ยังไม่ยืนยัน (ได้เฉพาะบัญชี Active) / ยังไม่ login ใหม่ · ไม่ต้อง restart |
| `/admin/*` 403 | role ใน token | login ใหม่หลังได้ role |
| ผู้สอนสร้างคอร์สไม่ได้ ไม่มีหมวดให้เลือก | `GET /api/catalog/categories` = `[]` | เจ้าของสร้างที่ `/admin/categories` |
| ส่งตรวจคอร์ส Live ไม่ผ่าน "คาบ N คาบยังไม่มีลิงก์ห้อง" | หน้า ตารางสอนสด (คาบที่ติดแถบเตือน) | วางลิงก์ให้ครบ (https จาก meet.google.com / zoom.us / teams.microsoft.com) |
| กดเชื่อม Google ได้ 503 `live.google_not_configured` | env Google | ตั้ง client (หัวข้อ 3) หรือใช้วางลิงก์ |
| Google: `redirect_uri_mismatch` | `Integrations__Google__RedirectUri` เทียบ console | ต้องตรงทุกตัวอักษร (`https`, host, path `/api/live/instructor/google/callback`) |
| Google: access blocked / ผู้สอนเชื่อมไม่ได้ | consent screen อยู่ Testing | เพิ่มอีเมลใน Test users หรือส่งตรวจสอบแอป |
| ผู้เรียนกดเข้าห้อง 409 `live.window_not_open` | — | ปกติ: เปิดก่อนเริ่ม 15 นาที (`Live__JoinWindowBeforeMinutes`) |
| ผู้เรียนที่ซื้อแล้วเข้าห้องได้ 404 | enrollment หมดอายุ/ถูกเพิกถอน/คนละคอร์ส | ตรวจ `LEARNING.ENROLLMENTS` · 404 ตั้งใจให้เหมือนไม่มีคาบนี้ (กันเดา id) |
| ผู้เรียนไม่ได้รับอีเมลเชิญ | อีเมลถูกซื้อ < 24 ชม. ก่อนคาบ? job รันไหม | เชิญส่งแน่ ๆ ภายใน ~2 นาทีเมื่อ job รัน · **reminder 24 ชม. จะไม่ส่ง** ถ้าเชิญหลังเข้ากรอบ 24 ชม. แล้ว (ตั้งใจ — อีเมลเชิญคือการแจ้งเตือนแล้ว) |
| จ่ายเงิน 503 `payment.provider_not_configured` | `Payment__Stripe__*` | ตั้ง key (production ต้อง `sk_live_`) |
| จ่ายแล้วแต่ไม่ได้สิทธิ์เรียน | log `/api/commerce/webhooks/stripe` · Stripe dashboard → Webhooks | URL/`whsec_`/events ผิด · สถานะที่ต้องการ `Succeeded` มาจาก webhook เท่านั้น |
| วิดีโอบันทึกการสอนเล่นไม่ได้ (CDN 404) | Bunny pull zone | เปิด Token Authentication + ตั้ง `VideoProvider__TokenAuthenticationKey` ตรงกัน (ยังไม่เคยพิสูจน์จนเล่นได้ — ดูข้อจำกัด) |
| error 500 ที่หน้าที่เกี่ยวกับอีเมล/แจ้งเตือนหลัง deploy | `dotnet ef migrations list` | ขาด `AddNotificationKafkaDelivery` → apply migration (หัวข้อ 2 ข้อ 1) |
| อีเมล "คุณถูกออกจากระบบ…" ถี่ผิดปกติ | — | จำกัด 2 อุปกรณ์/บัญชี (`Identity__Security__MaxConcurrentSessions`) ทุกครั้งที่ login เกินโควตาจะส่ง 1 ฉบับ — ปกติถ้าทดสอบ login รัว ๆ |

## 7) ข้อจำกัดที่รู้แล้ว (ยังไม่ใช่ blocker ของ go-live แต่ต้องรู้)

- **ผู้เรียนที่ผ่าน join gate แล้วส่งต่อลิงก์ห้องได้** — ลิงก์ Meet คือ secret ที่ส่งต่อได้ ระบบกันไม่ได้ในระดับ Google · ลดความเสี่ยงโดยตั้งห้องเป็น "ต้องให้เจ้าของห้องอนุญาต" (host must admit — UI แนะนำให้ผู้สอนแล้ว) · ระบบไม่ส่งลิงก์ห้องทางอีเมล/ICS/หน้าสาธารณะเลย (พิสูจน์แล้ว)
- **Google สร้าง Meet อัตโนมัติ: ทดสอบกับ Google จริงแล้ว (2026-10-09, เครื่อง dev)** — ผู้สอนเชื่อมบัญชี → สร้างคาบ → ภายใน ~1 นาทีได้ห้อง `meet.google.com` จริง และกิจกรรมโผล่ในปฏิทิน → ยกเลิกคาบ → กิจกรรมถูกลบจากปฏิทิน (`PendingDelete` → `Deleted`) · ยังไม่เคยทดสอบบน production (ต้องตั้ง `Integrations__Google__*` ที่นั่นก่อน) · แอป Google ยังไม่ผ่านการตรวจ ผู้สอนจะเห็นหน้า "ยังไม่ได้ยืนยันแอป" และจำกัด 100 คน
- **จ่ายเงินจริง (PromptPay/บัตร) จนจบ: ยังไม่เคยทดสอบ** ในรอบนี้ (ไม่มี Stripe key/webhook forwarding) — enroll ทดสอบด้วยโค้ด 100% (order → enrollment → อีเมลเชิญ ทำงานจริง) · ใบเสร็จ/ภาษี/revenue split หลังจ่ายจริงต้องทดสอบด้วยการซื้อจริง 1 ครั้ง
- **Bunny Token Authentication**: signed manifest เคยได้ 404 ที่ CDN บน dev (pull zone ยังไม่เปิด token auth) → การดูวิดีโอ/บันทึกย้อนหลังจนเล่นได้ยังไม่ผ่านการทดสอบ
- **บัญชีรับเงินผู้สอน (X-33): แก้แล้ว** — backend เพิ่ม `PUT /api/payout/instructor/payout-account` (upsert ของตัวเอง, แก้ข้อมูลบัญชี/ภาษี = ล้างสถานะ verified, ข้ามบัญชีนี้ในรอบจ่ายจนกว่าแอดมินตรวจใหม่) และ FE ย้ายขั้นนี้ไปหน้ารายได้หลังอนุมัติ · ยังต้องตัดสินใจก่อนรอบจ่ายแรก: (ก) ยังไม่มี audit trail ที่แก้ย้อนหลังไม่ได้เมื่อเปลี่ยนบัญชีธนาคาร (มีแค่ UpdatedAtUtc/UpdatedBy) (ข) ปุ่ม verify ของแอดมินไม่ผูกกับเวอร์ชันข้อมูล — แอดมินที่เปิดดูค่าเก่าแล้วกด verify หลังผู้สอนเพิ่งแก้ จะ verify ค่าใหม่ที่ไม่ได้เห็น
- ข้อความใน UI อีก ~60 ไฟล์ (หน้า admin อื่น/instructor/cart ฯลฯ) ยัง hardcode ภาษาไทยนอกไฟล์ i18n (หนี้เดิม ไม่เกี่ยวหน้าที่เพิ่มใหม่)
- `Siri.Workers/appsettings.json` มี `DataProtection` key แบบ dev placeholder — ถ้า deploy Workers ต้องส่ง key จริงตัวเดียวกับ API ทาง env เสมอ

## 8) ผลตรวจ gate นี้ (สรุป)

รันบน local (PG18 + Garnet + Mailpit, DB ว่างใหม่, **API process เดียว `Hangfire__ServerInApi=true` ไม่มี Workers**): สมัคร/ยืนยันอีเมลผ่าน UI+Mailpit (14 วินาที) · owner bootstrap · หน้า admin ทั้งสาม · ผู้สอนสมัคร→อนุมัติ→สร้างคอร์ส Live 3 คาบ→วางลิงก์→ส่งตรวจ→อนุมัติ · หน้า public ไม่มีลิงก์ห้อง · enroll ผ่านโค้ด 100% · อีเมลเชิญ+ICS ภายใน ~1 นาที · reminder 24 ชม. · อีเมลเลื่อนเวลา/ยกเลิก (ICS CANCEL) · join gate (ใน/นอก window, ไม่ได้ลงทะเบียน = 404 เหมือน session ไม่มีจริง, anonymous 401) · dashboard ผู้สอนตัวเลขจริง · header/avatar หลัง hard reload · `/admin/live-status` ไม่มีคำเตือนนอกจากที่คาดไว้ · `Live__Provider=GoogleMeet` ไม่มี credential → ตัดสินห้องทันที · ตัวเลข test ละเอียดอยู่ที่ `docs/PROGRESS.md` บล็อก 2026-10-09 (integrator-qa final gate)

## 9) นำเข้าบันทึก Google Meet อัตโนมัติ (P11-13) — ปิดอยู่เป็นค่าเริ่มต้น

> สัญญา: `docs/contracts/P11-13-live-recording-auto-import.md` · เจ้าของระบบเปิดเองเมื่อมีบัญชี Google Workspace จริง · **ฝั่ง Bunny (อัปโหลด/transcode/ลบ) QA พิสูจน์บน dev แล้ว; ฝั่ง Google Meet/Drive/`hd`/Restricted scope ยังไม่เคยพิสูจน์จนกว่าจะมีบัญชี Workspace จริง** (ดู 9.6)

**พฤติกรรม:** ระบบ *ตรวจเอง ไม่ถามผู้สอน* ว่าบัญชี Google ที่เชื่อมเป็นแบบไหน (ดูจาก `hd` ของ userinfo) —
- **บัญชีส่วนตัว (Gmail ฯลฯ)** → ไม่มีอะไรอัตโนมัติ ผู้สอนอัปโหลดบันทึกเองเหมือนเดิม (คาบที่จบแล้วในหน้าจัดการคาบสอนมีปุ่มอัปโหลดอยู่แล้ว)
- **Workspace แต่ยังไม่ยินยอมสิทธิ์บันทึก** → เหมือนบัญชีส่วนตัว + มีการ์ด "เปิดนำเข้าบันทึกอัตโนมัติ" ที่ `/instructor/live-settings` (Google consent รอบที่ 2 แบบไม่บังคับ)
- **Workspace + ยินยอมแล้ว + เปิด flag** → หลังคาบจบ ~10 นาที job `live-recording-import` (ทุก 5 นาที) ค้นไฟล์บันทึกใน Google → คัดลอก Drive → Bunny Stream → รอ transcode → แนบเป็นบทเรียนของคอร์ส → อีเมล+แจ้งเตือนผู้สอน "เพิ่มบันทึกแล้ว"
- **ปิด flag (`Enabled=false`)** → `recordingImport.mode` เป็น `Manual` ทุกคาบ แม้คาบนั้นเคยมีแถวนำเข้าเก่า (สถานะ/รหัสของแถวเก่ายังแสดงเป็นประวัติ แต่ "ลองใหม่" ใช้ไม่ได้) · การอ่านสถานะ Google ของผู้สอนไม่เรียก Google ออกไปหาชนิดบัญชี (ไม่ refresh token ไม่เรียก userinfo) — ชนิดบัญชีของแถวเก่าจึงค้างเป็น `Unknown` จนกว่าจะเปิด flag

**โครงสร้างงาน (สำคัญตอนดู Hangfire):** มีสองส่วนที่ตั้งใจแยกกัน
1. **tick** `live-recording-import` — recurring ทุก 5 นาที **สั้นและไม่เคยคัดลอกไฟล์**: ค้นหา session ใหม่ (ถาม Catalog เฉพาะคาบของผู้สอนที่เข้าข่ายอัตโนมัติ — บัญชี Workspace ที่ active + มี scope บันทึกครบ — ในหน้าต่าง 48 ชม. ทีละ 200 คาบ สูงสุด 10 หน้าต่อรอบ จึงไม่ถูกคาบของผู้สอนที่ใช้ทางอัปโหลดเองบดบัง), ค้นไฟล์ใน Google, ตรวจสถานะ transcode, ยึดคืนแถวที่ lease หมด · เมื่อเจอไฟล์ จะ *claim* แถว (`Transferring` + lease) แล้ว **queue background job** `LiveRecordingTransferJob.RunAsync(importId)` หนึ่งตัวต่อหนึ่ง claim · tick หยุดเริ่มแถวใหม่หลัง ~100 วินาที (ต่ำกว่า lock timeout 120 วินาที) ดังนั้น tick ที่ทับกันไม่ fail และ admin status ไม่ขึ้น `recurring_job_failing` เพราะไฟล์ใหญ่
2. **transfer job** — fire-and-forget ต่อ 1 import ทำการคัดลอก Drive → Bunny จริง (นานได้เป็นชั่วโมง) · ไม่มี automatic retry (state machine ของแถวเป็นคนควบคุมการลอง) · มี storage lock **ต่อ import** (รอได้สูงสุด 15 นาที) เพื่อไม่ให้สอง job คัดลอกไฟล์เดียวกันพร้อมกัน · เห็นใน Hangfire dashboard ในคิว `default` · ทำงานบน server ไหนก็ได้ที่มี Hangfire (Workers แยก หรือ API เมื่อ `Hangfire__ServerInApi=true`)
- ถ้า process ตายระหว่าง claim กับ queue (ช่วงมิลลิวินาที) หรือ job หาย: แถวค้าง `Transferring` จน lease หมด (`TransferLeaseMinutes`) แล้ว tick ยึดคืน (นับ 1 attempt) และ queue ใหม่ — เหมือนเดิมทุกประการ

### 9.1 ลำดับ deploy

1. **apply migration `20261009203126_AddLiveRecordingImport` ก่อน image ใหม่** (additive ล้วน: ตาราง `LIVE.SESSION_RECORDING_IMPORTS` + คอลัมน์ `HOSTED_DOMAIN`, `ACCOUNT_KIND_CHECKED_AT_UTC` บน `LIVE.INSTRUCTOR_GOOGLE_ACCOUNTS`; ไม่มี drop/rename) · migration นี้อยู่ **หลัง** `AddPaymentAmountOverride` ในลำดับ (คนละงาน แต่ใช้ bundle เดียวกัน) · ถ้า image ใหม่ขึ้นก่อน → หน้าสถานะ Google และรายการคาบสอนของผู้สอน error 500 เพราะคอลัมน์/ตารางไม่มี · ห้าม apply ด้วยมือบน DB จริงโดยไม่รู้ตัว — เป็นคำสั่งของเจ้าของระบบ (`scripts/migrate-bundle.sh`)
2. rollout image ใหม่ โดย **ยังไม่ตั้ง flag** — ทุกอย่างทำงานเหมือนเดิม (ผู้สอนทุกคนเห็นแค่ทางอัปโหลดเอง)
3. ทำ Google Cloud ตามข้างล่าง แล้วค่อยตั้ง `Live__Recording__AutoImport__Enabled=true` + rollout

### 9.2 Config (section `Live:Recording:AutoImport`, ตั้งให้ **API และ Workers ทั้งคู่** ถ้ามี Workers)

| env | default | ความหมาย |
|---|---|---|
| `Live__Recording__AutoImport__Enabled` | `false` | **ปุ่มหลัก** · ปิด = ไม่ค้นหา ไม่สร้างแถว ไม่มีปุ่มขอสิทธิ์ ทุกคนเห็นแค่ทางอัปโหลดเอง · ปิดเมื่อไรก็ได้ (แถวที่ค้างอยู่หยุดถูกประมวลผลเฉย ๆ) |
| `__FirstSearchDelayMinutes` | 10 | ค้นครั้งแรกหลังเวลาจบคาบตามตาราง |
| `__SearchWindowHours` | 12 (สูงสุด 72) | หาต่อนานเท่านี้หลังจบคาบ แล้วจบเป็น `NoRecording` (ให้ผู้สอนอัปโหลดเอง) |
| `__MaxAttempts` | 6 | ความล้มเหลวชั่วคราวติดกัน (เครือข่าย/5xx/429) ก่อนเป็น `Failed` |
| `__MaxFileSizeMegabytes` | 8192 | ไฟล์ใหญ่กว่านี้ถูกปฏิเสธ (`file_too_large`) |
| `__TransferLeaseMinutes` | 180 | การคัดลอกที่ไม่จบภายในเวลานี้ถือว่าค้าง → job ยึดคืนและเริ่มใหม่ (นับเป็น 1 attempt) |
| `__BatchSize` | 5 | จำนวนแถวต่อรอบ job |
| `__DevSampleFilePath` | ว่าง | **dev เท่านั้น** (`Live__Provider=Logging`) · **production ต้องว่าง** — `ProductionConfigurationGuard` ไม่ยอมบูตถ้ามีค่า |

ทุกค่าบวก (`SearchWindowHours` ≤ 72) — ผิดจะบูตไม่ขึ้น (`ValidateOnStart`) · ใช้ Bunny ที่ตั้งไว้แล้ว (`VideoProvider__*` หัวข้อ 3) ไม่มี key ใหม่

### 9.3 Google Cloud (เพิ่มจากหัวข้อ 3)

1. Enable **"Google Meet REST API"** และ **"Google Drive API"** ใน project เดียวกับ Calendar
2. OAuth consent screen เพิ่ม scope สองตัว: `https://www.googleapis.com/auth/meetings.space.readonly` (**Sensitive**) และ `https://www.googleapis.com/auth/drive.meet.readonly` (**Restricted**)
3. ⚠️ **Restricted scope ต้องผ่านการตรวจสอบของ Google** (แอปที่เก็บ/ส่งต่อข้อมูลจาก Drive ไปเซิร์ฟเวอร์ของเราต้องทำ security assessment ก่อนรองรับผู้ใช้เกิน 100 คน — ตรวจเงื่อนไข/ค่าใช้จ่าย/ขั้นตอนล่าสุดใน console เสมอ ก่อนวางแผนเปิดให้ผู้สอนทุกคน) · ระหว่างยังไม่ตรวจสอบ: ผู้สอนที่กดยินยอมเห็นหน้า "แอปนี้ยังไม่ได้รับการยืนยัน" และจำกัด **≤ 100 บัญชี** (นับตลอดอายุแอป) · ถ้า consent screen ยังเป็นสถานะ Testing ต้องเพิ่มอีเมลผู้สอนใน Test users · refresh token ของแอปสถานะ Testing หมดอายุเร็ว (ดูหัวข้อ 3)
4. **Redirect URI เดิมใช้ต่อได้** (callback ตัวเดียวกัน `…/api/live/instructor/google/callback` จบทั้งสอง consent)
5. ฝั่ง Workspace: ต้องเป็นแพ็กเกจที่ **เปิดการบันทึก Meet ได้** และผู้จัด (ผู้สอน) ต้อง **กดบันทึกจริง** — `hd` บอกแค่ "เป็น Workspace" ไม่ได้บอกว่าอัดได้ · ถ้าไม่มีไฟล์ในช่วงค้นหา → สถานะ `NoRecording` (แถวคาบสอนบอกผู้สอนให้อัปโหลดเอง/ลองใหม่ — ไม่ส่งอีเมล เพราะคาบที่ไม่ได้กดบันทึกไม่ใช่ข้อผิดพลาด)

### 9.4 อ่านสถานะ / แก้ปัญหา

ผู้สอนเห็นสถานะของแต่ละคาบ (`recordingImport` ในหน้ารายการคาบ) — เป็น **รหัสเท่านั้น** ไม่มี id/URL ของ Google หรือ Bunny · tick `live-recording-import` อยู่ใน `/admin/live-status` แล้ว (ล้มเหลว/เลยเวลา = ขึ้น `recurring_job_failing`/`recurring_job_overdue` เหมือนงานอื่น; ตอนปิด flag มันแค่ no-op และขึ้น "รันล่าสุด" ปกติ) · ส่วน transfer job ดูใน Hangfire dashboard · log ค้นด้วยคำว่า `Live recording import`

| สถานะ (`ERROR_CODE`) | ความหมาย / ทำอะไร |
|---|---|
| `Waiting` | ยังหาไฟล์ไม่เจอ ค้นซ้ำห่าง 10→20→40 นาที แล้วทุกชั่วโมงจนครบ `SearchWindowHours` |
| `Transferring` / `Processing` | กำลังคัดลอก / รอ Bunny transcode (ไม่เกิน 6 ชม. → `transcode_timeout`) |
| `Attached` | เสร็จ — บทเรียนอยู่ในคอร์ส ผู้เรียนที่ลงทะเบียนดูได้ทันที |
| `NoRecording` (`no_recording_found`, `drive_file_not_found`) | ไม่พบไฟล์ → ผู้สอนอัปโหลดเอง หรือกด "ลองใหม่" ภายใน 30 วัน |
| `NeedsReconnect` (`recording_scope_missing`, `invalid_grant`, `google_account_unavailable`) | Google อ่านบันทึกไม่ได้ → ผู้สอนเชื่อม Google ใหม่/กดยินยอมสิทธิ์บันทึกอีกครั้ง แล้วกด "ลองใหม่" |
| `Failed` (`file_too_large`, `transcode_failed`, `transcode_timeout`, `ingest_failed`, `transfer_timeout`, `google_*`, `attach_failed`) | เลิกลอง → อีเมลบอกผู้สอน (อัปโหลดเอง หรือ "ลองใหม่") · `google_client_misconfigured` = ปัญหา OAuth client/คีย์เข้ารหัสฝั่งเรา ดู log |
| `Skipped` | ไม่มีอะไรต้องนำเข้า: คาบถูกยกเลิก / มีบันทึกอยู่แล้ว (ผู้สอนอัปโหลดเอง) / บัญชีไม่ใช่ Workspace / ห้องไม่ใช่ห้อง Google ที่ระบบสร้าง |

- แถวนับจาก `LIVE.SESSION_RECORDING_IMPORTS` (1 แถวต่อ 1 คาบ) · แถวไม่เคยถูกลบ · retry คือเปลี่ยน `Failed`/`NoRecording`/`NeedsReconnect` กลับเป็น `Waiting`
- ถ้า pod restart/deploy **กลางการคัดลอก** แถวจะค้าง `Transferring` จน lease หมด (`TransferLeaseMinutes`, default 180 นาที) แล้ว tick ยึดคืนและ queue การคัดลอกใหม่ทั้งไฟล์ (นับเป็น 1 attempt) — ลดค่านี้ได้เพื่อให้กู้เร็วขึ้น แต่ต้องมากกว่าเวลาคัดลอกจริงของไฟล์ที่ใหญ่ที่สุดที่คาดไว้ · ตอนนี้ API pod เป็นคนรัน job (Hangfire server ใน API) การคัดลอกจึงกินแบนด์วิดท์ขาออก/ขาเข้าของ pod นั้น (สตรีม ไม่กินหน่วยความจำ)
- ⚠️ **ข้อจำกัดที่รู้แล้ว (F3): ถ้ากระบวนการล้ม/restart กลางการคัดลอก จะเหลือ asset สถานะ `Uploading` (และวิดีโอที่อัปโหลดมาครึ่งเดียวที่ Bunny) ค้างอยู่ในคลังสื่อของผู้สอน** — แถวนำเข้าไม่รู้ id ของ asset นี้ (ระบบคืน id ให้แถวหลังคัดลอกสำเร็จเท่านั้น และไม่มี contract สำหรับลบ asset) การคัดลอกรอบใหม่สร้าง asset ใหม่ ไม่ใช้ตัวครึ่งทางซ้ำ · **ผู้สอนลบ asset ที่ค้างได้เองจากคลังสื่อ** (การลบผ่านคลังสื่อลบวิดีโอที่ Bunny ด้วย) · ไม่กระทบคอร์สหรือบทเรียนของผู้เรียน (asset นี้ยังไม่ได้แนบกับบทเรียนใด)

### 9.5 ความเป็นส่วนตัว / ความเสี่ยงที่ต้องรู้

- บันทึกมีใบหน้า/เสียงผู้เรียน และกลายเป็นบทเรียนที่ **ผู้เรียนที่ลงทะเบียนทุกคนดูได้ทันที** — ระบบอีเมลบอกผู้สอนทุกครั้งที่เพิ่ม · คอร์สที่ขายแล้วลบบทเรียนไม่ได้ (domain กันไว้) ผู้สอนแทนที่ได้ด้วยการแนบบันทึกอื่นแทน
- ไม่มี token / รหัสห้อง Meet / id ไฟล์ Drive / id ของ Bunny / URL ใน log หรือ response ใด ๆ · ไฟล์ถูกสตรีมจาก Drive ไป Bunny ไม่เก็บทั้งไฟล์ในหน่วยความจำหรือดิสก์ของเรา
- การยินยอมรอบที่ 2 ไม่เพิกถอน token เดิมของ Google (เป็น grant เดียวกัน) — ถ้าผู้สอนติ๊กไม่ครบสองสิทธิ์ การเชื่อมปฏิทินเดิมยังใช้ได้และได้เหตุผล `recording_scope_missing`

### 9.6 ยังไม่ผ่านการพิสูจน์ (ตรงไปตรงมา)

- **พิสูจน์แล้ว (QA, dev):** Bunny — อัปโหลด, transcode, ลบ — และ integration ของฟีเจอร์นี้กับ PostgreSQL/Redis จริงผ่าน `Live:Provider=Logging` (ไฟล์ตัวอย่างสังเคราะห์)
- **ยังไม่เคยพิสูจน์:** พฤติกรรมของ **Google Meet REST / Drive / `hd` / Restricted scope** กับบัญชี Google Workspace จริง (ยังไม่มีบัญชี Workspace) — การหาไฟล์บันทึก, รูปแบบ response จริง, หน้า consent และขั้นตอนตรวจสอบแอปของ Google · และการคัดลอกไฟล์ขนาดหลาย GB จริงจนจบ
- **ทดสอบกับบัญชี Workspace จริงอย่างน้อย 1 คาบก่อนบอกผู้สอนว่าใช้ได้**
