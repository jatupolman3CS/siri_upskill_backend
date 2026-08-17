# Frontend Rules — Angular 22 + TypeScript

## Component

- **Standalone เท่านั้น** ห้ามสร้าง `NgModule` ใหม่
- `changeDetection: ChangeDetectionStrategy.OnPush` ทุก component (แอปเป็น zoneless)
- ไฟล์ template แยกเมื่อเกิน ~30 บรรทัด; style ใช้ Tailwind utility เป็นหลัก
- `input()` / `output()` / `model()` แบบ signal เท่านั้น — ห้าม `@Input()` / `@Output()` decorator แบบเก่า
- ใช้ control flow ใหม่ `@if` `@for` `@switch` `@defer` — ห้าม `*ngIf` `*ngFor`
- `@for` ต้องมี `track` ที่เป็น id จริงเสมอ (ห้าม track index ถ้ารายการเรียงใหม่ได้)
- Component ไม่เกิน ~200 บรรทัด ถ้าเกินให้แยก

## State

- **Signals เป็นค่าเริ่มต้น**: `signal()`, `computed()`, `linkedSignal()`, `resource()` / `httpResource()`
- RxJS ใช้เฉพาะ event stream จริง (search debounce, websocket, player event) — ไม่ใช่เก็บ state
- ห้าม subscribe ใน component โดยไม่ cleanup — ใช้ `takeUntilDestroyed()` หรือแปลงเป็น signal
- Global state ต่อ feature = service ที่ provide ด้วย signal (ยังไม่ต้องใช้ NgRx จนกว่าจะพิสูจน์ว่าจำเป็น)
- ห้าม mutate object ใน signal ตรง ๆ — `.set()` / `.update()` ด้วยค่าใหม่เสมอ

## Data access

- Component **ห้ามเรียก `HttpClient` ตรง** — ผ่าน `*ApiService` ใน `features/*/data/` เท่านั้น
- ทุก response ต้องมี TypeScript interface (สร้างจาก OpenAPI ได้ยิ่งดี) — **ห้ามใช้ `any`**
- จัดการ loading / error / empty state ให้ครบทุกหน้าจอที่ดึงข้อมูล ไม่ใช่แค่ happy path
- Auth token ใส่โดย interceptor เท่านั้น; **access token เก็บใน memory ห้ามลง localStorage**; refresh อยู่ใน httpOnly cookie

## SSR (สำคัญ — หน้า public ทำ SEO)

- ห้ามแตะ `window`, `document`, `localStorage`, `navigator` ที่ระดับ top-level หรือใน constructor
- ต้องอยู่ใน `afterNextRender()` / `afterRenderEffect()` หรือ guard ด้วย `isPlatformBrowser(inject(PLATFORM_ID))`
- หน้า public (home, catalog, course detail, blog) ต้องตั้ง title/meta/canonical/JSON-LD ครบ
- `/learn`, `/instructor`, `/admin` เป็น CSR + `noindex`

## Performance

- Route ทุกอันเป็น lazy `loadComponent` / `loadChildren`
- `@defer` สำหรับ block หนัก (video player, chart, comment thread) พร้อม `@placeholder` ที่กันขนาดไว้ (กัน CLS)
- รูปทุกใบผ่าน `NgOptimizedImage` + กำหนด width/height เสมอ
- งบขนาด bundle: initial ≤ 300KB gzipped — ถ้าเกินให้แจ้ง อย่าปล่อยผ่าน

## Forms

- Typed reactive forms เท่านั้น (ห้าม template-driven, ห้าม `FormGroup<any>`)
- Error แสดงติดกับ field ที่ผิด และแสดงเมื่อ touched/dirty แล้วเท่านั้น
- ปุ่ม submit ต้องกันกดซ้ำระหว่างส่ง

## i18n

- ห้าม hardcode ข้อความไทย/อังกฤษใน template หรือ .ts — ใช้ i18n key เสมอ
- วันที่/เงิน format ผ่าน pipe ที่ตั้ง locale `th-TH` และโซนเวลา `Asia/Bangkok` (ข้อมูลจาก API เป็น UTC)

## TypeScript

- `strict: true` ทั้ง repo — ห้ามปิด; ห้าม `any`, ห้าม `@ts-ignore` (ถ้าจำเป็นจริงต้องมี comment อธิบายเหตุผล)
- ห้าม `!` non-null assertion ยกเว้นพิสูจน์ได้ชัดว่าปลอดภัย
- `unknown` + type guard แทน `any` เมื่อรับข้อมูลจากภายนอก

## Testing

- Unit test: service ที่มี logic, signal computed, guard, interceptor
- E2E (Playwright): flow สำคัญ — สมัคร/ล็อกอิน, ค้นหาคอร์ส, ซื้อคอร์ส, ดูวิดีโอ+resume, ทำ quiz
- ห้าม assert ด้วย CSS class ที่เป็น style — ใช้ role/label (ได้ทั้ง test และ a11y)
