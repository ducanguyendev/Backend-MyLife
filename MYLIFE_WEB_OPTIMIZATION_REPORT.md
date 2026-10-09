# MyLife Web Optimization Report

Ngày kiểm tra: 07/10/2026. Phạm vi: Backend-MyLife và Frontend-MyLife, branch feature_minhduc.

Đã hoàn thành private FamilyTree theo USER, tối ưu truy vấn/bundle/ảnh, bảo vệ runtime và cải thiện UX. Backend 94/94, frontend Node 47/47, storage harness 42/42, browser regression PASS. Build Release, production frontend và lint PASS. Migration mới CHƯA apply vào database ứng dụng; dừng để review.

Report này thay thế các nhận định multi-role trong FAMILYTREE_USER_CATEGORY_CRUD_REPORT.md của lượt trước. Report cũ được giữ như lịch sử. Business model hiện tại là ADMIN XOR USER.

## Bằng chứng trước / sau

Kích thước dưới đây là output production Vite thực đo, đơn vị kB thập phân, không phải thời gian tải mạng. Main chunk không đại diện toàn bộ initial JavaScript.

| Hạng mục | BEFORE | AFTER cuối cùng |
| --- | --- | --- |
| Main/index JS | 1.039,91 kB; gzip 311,99 kB | 384,10 kB; gzip 117,45 kB |
| Largest JS chunk | index 1.039,91 kB | index 384,10 kB |
| FamilyTree route | eager trong main | FamilyTreePage 92,00 kB; gzip 21,75 kB |
| Admin route | eager trong main | AdminDashboard 19,50 kB; gzip 5,58 kB |
| Mindmap | eager, gồm @xyflow/react/dagre/html-to-image | FamilyMindmap 277,29 kB; gzip 89,02 kB; chỉ import khi chọn mindmap |
| Register | eager | 15,71 kB; gzip 4,63 kB |
| Shared chunk lớn | nằm main | LanguageSwitcher 180,77 kB; gzip 59,88 kB |
| Initial CSS | 107,20 kB; gzip 17,15 kB | 92,16 kB; gzip 14,92 kB; Mindmap CSS riêng 15,41 kB |
| Artificial loading | unconditional 1.200 ms | 0 ms; chờ auth, import, API thật |
| Gallery fallback | Drive thumbnail w1600 | w640; lazy + async; lightbox giữ chất lượng cao |
| FamilyTree ownership | global members, không owner | server USER → unique FamilyTree → members |
| Read query | tracking + nhiều collection Include trong joined query | tree filter + AsNoTracking + AsSplitQuery; DTO giữ tương thích |
| Cycle validation | load toàn hệ thống; repeated matching | projection Id/FatherId/MotherId/SpouseId trong một tree; Dictionary/HashSet |

Đã duyệt closure static imports trong manifest: initial JS hiện 644.681 bytes (644,681 kB) qua 11 files, bao gồm shared UI/auth/i18n. Vì vậy không dùng 384,10 kB để tuyên bố toàn bộ initial download. Initial closure không chứa FamilyTreePage/AdminDashboard/FamilyMindmap; closure của FamilyTreePage không chứa Mindmap. Không tăng chunkSizeWarningLimit; không còn JS chunk >500 kB. Không tạo benchmark API/render milliseconds từ môi trường này.

## 85 mục review bắt buộc

| # | Mục | Kết quả và quyết định |
| --- | --- | --- |
| 1 | Local git state trước sửa | Backend: 9 tracked modified + 2 untracked; frontend: 18 tracked modified + 1 untracked; cả hai feature_minhduc. Đã snapshot diff trước sửa vào temp và đọc source local, không lấy HEAD thay thế local work. Danh sách ở cuối report. |
| 2 | Existing features reuse | Reuse USER-only FamilyTree/Library controllers, retired Admin alias, category PUT/name-only DTO, stable slug, error-code i18n, LibraryImage, storage clients, shared Modal và các suites hiện có. Không tạo admin module mới. |
| 3 | ADMIN XOR USER | AppRoles.ExclusiveRole yêu cầu đúng một supported role. Login/Google/refresh/JWT từ chối role ambiguity; web quyết định theo primary role; roles array tương thích chỉ singleton. Admin role update tiếp tục thay thế role. Không migrate/xóa UserRoles của application DB. Không combined-role fixture. |
| 4 | FamilyTree BEFORE | FamilyMember/FamilyRelationship là global; USER authorization có nhưng chưa phân ownership giữa USERs. Không có FamilyTree entity/table. |
| 5 | FamilyTree AFTER | User 1 → 0/1 lazy FamilyTree → many FamilyMembers; mỗi USER dùng feature có đúng một tree private. ADMIN không đi qua helper qua HTTP feature. Không sharing/multiple trees. |
| 6 | Entity/schema | Shared/Entities/FamilyTree.cs: long Id, int OwnerUserId, OwnerUser, UTC CreatedAt/UpdatedAt, Members. family_trees: bigint identity PK, owner_user_id integer NOT NULL UNIQUE, timestamps timestamptz NOT NULL, FK users.id RESTRICT. Timestamps khởi tạo khi tree được tạo; chưa có tree metadata editing API. |
| 7 | FamilyMember FamilyTreeId | long bắt buộc + navigation; family_members.family_tree_id bigint NOT NULL, không default 0; FK RESTRICT. Không expose ownership trong write DTO. |
| 8 | Migration filename | Migrations/20261007080251_AddPerUserFamilyTrees.cs và Designer.cs; snapshot cập nhật. Không sửa migration lịch sử nào. |
| 9 | Up | Guard family_members phải empty; tạo family_trees và unique owner; add member ownership column, tree/member FK và indexes. Không drop/recreate members, seed trees, backfill, sửa Library/Avatar schema. Đã loại defaultValue 0 từ scaffold. |
| 10 | Down | Drop member→tree FK, hai member ownership indexes, family_tree_id, rồi family_trees (owner index/FK đi cùng table). Đã test trong disposable schema. Down làm mất ownership metadata, không dùng tùy tiện sau khi có dữ liệu thật. |
| 11 | Xác nhận empty | SELECT trong BEGIN READ ONLY trên application DB: 4 users, 0 family_members, 0 account không đúng một ADMIN/USER; migration cuối 20261006090341_AddLibraryCategories. Không xuất credentials/config vào report. |
| 12 | Legacy ownership | Không legacy backfill, không gán toàn bộ members cho account bất kỳ. Guard migration chủ động fail nếu precondition empty đã thay đổi trước operator apply. |
| 13 | Lazy creation | EnsureUserFamilyTreeAsync(userId, ct): no-tracking lookup, return nếu có; missing mới tạo với server owner. Migration không tạo tree cho bốn account. Generations-only GET không cần tree. |
| 14 | Concurrency | Transaction khóa users row FOR UPDATE, re-query rồi create; unique owner là final database guard. 12 first GETs đồng thời PASS, đúng 1 row/no unhandled 500. |
| 15 | Current user | ICurrentUserService dùng validated user ID trong HttpContext.Items từ JWT; fallback active email projection khi cần. FamilyTree/Library reuse, tránh duplicate user lookup sau OnTokenValidated. Không nhận ID từ client. |
| 16 | Interface | Mọi member CRUD trong IFamilyTreeService nhận userId và CancellationToken. Generation lookup giữ global + ct. Controller propagate RequestAborted; EF async all the way. |
| 17 | Cross-user | List/detail/update/delete filter tree; bilateral foreign direct IDs trả 404 FAMILY_MEMBER_NOT_FOUND, không báo belongs-to-other-user. Injected ownership fields bị bỏ qua. Dữ liệu chủ sở hữu không đổi. |
| 18 | Parent/spouse/children | Validate mọi ID có trong current-tree projection trước mutation; foreign references POST/PUT →400 FAMILY_RELATED_MEMBER_NOT_FOUND. Spouse/children synchronization lookups đều scoped. |
| 19 | Horizontal | Cả hai endpoints phải ở tree hiện tại; validate foreign before write; sync query kiểm tra cả Member1/Member2 tree. Existing unique composite giữ nguyên. |
| 20 | Cycle scope | Proposed graph gồm simultaneous parent/child edits, chỉ current tree; Dictionary + complete/visiting HashSets, giữ cycle/self/duplicate-parent/spouse-in-use validation. |
| 21 | Delete cleanup | Spouse/parent/children references xử lý trong same tree; horizontal links dùng existing member FK cascade cho links incident to own authorized member. Các writes API không thể tạo cross-tree links; không load/delete foreign members. |
| 22 | Generation | Giữ global system reference 1–5, no ownership migration. Member Generation vẫn lọc AND với search ở UI; owner/generation index hỗ trợ query. |
| 23 | Member avatar ownership | Audit: hiện chỉ có IGoogleDriveMemberAvatarService storage contract, không HTTP member upload/delete endpoint hoặc persisted file-ID field. Member CRUD avatarUrl đã scoped. Không mở feature upload mới; HTTP flow tương lai phải authorize member bằng tree trước storage và trả foreign 404. Storage roots/identity vẫn tách account/member/Library. |
| 24 | FamilyTree indexes | Unique owner_user_id, member family_tree_id và (family_tree_id,generation) mới; father/mother/spouse indexes hiện có giữ nguyên. Migration schema tests verify. |
| 25 | Relationship indexes | Unique (Member1Id,Member2Id,RelationType) hỗ trợ Member1 leading key; Member2 index EF hiện có. Không thêm duplicate Member1 index hoặc đổi relation schema. |
| 26 | Library indexes | Giữ (created_by_user_id,updated_at), (album_id,sort_order,created_at), unique (created_by_user_id,slug) và Drive identity uniques. Không Library migration mới; CreatedByUserId ownership giữ nguyên. |
| 27 | EF BEFORE | MembersQuery tracking, Includes father/mother/spouse và nhiều collections; không owner filter; validation graph global. |
| 28 | EF AFTER | Scoped read root trước collection Include, no-tracking, split collections; validation tiny projection/dictionary. Mutation queries vẫn tracking trong serializable transactions để giữ integrity. |
| 29 | AsNoTracking | Member reads, generation, tree fast lookup, reference/cycle projection và JWT projection đều no-tracking. Không áp dụng no-tracking cho mọi query cho entity đang sửa. |
| 30 | Split/projection | Chọn AsSplitQuery cho read DTO nhiều nav để tránh collection cartesian join và giữ payload; không N+1 per member. Graph/JWT dùng projection nhỏ. Split reads có nhiều roundtrips và có thể quan sát thay đổi giữa statements; cân nhắc snapshot transaction/DTO projection khi tải lớn, chưa benchmark latency. |
| 31 | Search | FullName, Role, PhoneNumber, Address; generation AND. Client-side, no pagination/debounce artificial. |
| 32 | Normalize | memberSearch.ts trim/lowercase/remove Vietnamese tones (kể cả Đ/đ); normalize query một lần cho filter. Tests nguyen/NGUYEN/truong toc/0901/ho chi minh PASS. |
| 33 | Search i18n | VI: Tìm tên, vai trò, SĐT, địa chỉ...; EN: Search name, role, phone, address...; dùng admin.search_member hiện có. |
| 34 | Surname | Badge generic admin.my_family_tree: Gia phả của tôi / My family tree. Không hardcode họ Nguyễn cho user tree; không thêm rename-tree feature. |
| 35 | States | FamilyTree initial request có AbortSignal; loading/error+retry/true-empty+add-first/filtered-no-match/success riêng. API failure không masquerade empty; retry chỉ refetch, bỏ late aborted response. Library states và action loading giữ nguyên. |
| 36 | Routes | React.lazy + Suspense FamilyTreePage/AdminDashboard/Register. ProtectedRoute chờ auth và role trước render content; bốn FamilyTree aliases vẫn USER-only; Admin không mount family API. |
| 37 | Mindmap | Dynamic import chỉ viewMode mindmap; bỏ heavy eager barrel exports. Giữ graph, node CRUD/details, MiniMap, orientation TB/LR, exportPNG. Không rewrite graph. |
| 38 | Bundle BEFORE | Production baseline main/largest 1.039,91 kB; gzip 311,99; routes/Mindmap eager; warning >500kB. Log lưu temp. |
| 39 | Bundle AFTER | Main 384,10; FamilyTree 92,00; Admin 19,50; Mindmap 277,29 kB. Gzip và static closure được ghi trong bảng thực đo trên. Không tăng ngưỡng warning. |
| 40 | Fake delay | Xóa unconditional setTimeout1200; không thay fake delay khác. Loading chỉ actual auth/import/API. LoadingScreen status localized, reduced-motion không lặp decorative animation. |
| 41 | Library images | variant thumbnail/lightbox; gallery known lh3/Drive URLs và fallback w640 lazy/async; lightbox giữ primary/high-res fallback w1600 eager. Dedupe candidates/stale error/placeholder và original Drive view giữ nguyên. Không sửa stored URL/file ID/Drive bytes. |
| 42 | Member images | Card/list/node thêm lazy + decodingasync; detail ảnh hiện có giữ nguyên. Không tự rewrite arbitrary external avatarURL; không storage/image resize feature mới. |
| 43 | Rate policies | Built-in fixed-window, queue 0: Auth10/min (login/register/google chung policy), Refresh30/min, Upload10/min (Library+account avatar). PermitLimit/WindowSeconds configurable; cấu hình nonpositive fail. Không limiter cho Library GET. |
| 44 | Partitions | Auth/Refresh theo RemoteIpAddress; uploads theo validated numeric user ID, fallback IP. Không tin raw X-Forwarded-For. In-process per-instance limits: cần review trusted proxy và shared edge policy khi scale nhiều instances. |
| 45 | 429 i18n | code RATE_LIMITED + Retry-After; feature mapper/API helper/login/register/Google localized VI/EN. Known rate code không render raw diagnostic message. |
| 46 | Upload limits | Reuse 20 files ×5 MiB +1MiB overhead =101 MiB request (105.906.176 bytes), RequestSizeLimit và MultipartBodyLengthLimit khớp constant. Per-file/MIME/signature/extension/empty checks giữ nguyên JPEG/PNG/WebP/GIF. Account avatar5 MiB rules giữ nguyên. Reverse proxy phải cho body limit tương ứng nếu bật batch20. |
| 47 | Compression | Built-in gzip+Brotli enable HTTPS cho /api/family-tree, /api/library, /health suitable MIME. Image formats không compress; token-issuing auth excluded. Gzip integration thực decompressed JSON PASS. Brotli registration audited; chưa riêng test negotiate br. |
| 48 | OutputCache | Intentionally không thêm output caching cho authenticated user-specific /family-tree, /library, /me. Không có public response cần cache đủ rõ để thêm framework caching. |
| 49 | Live | /health/live process only, không DB; 200 Healthy ngay cả readiness DB unavailable trong test. |
| 50 | Ready | /health/ready scoped CanConnectAsync PostgreSQL;200 Healthy/503 Unhealthy JSON chỉ status. Không gọi Drive hay lộ config/stack. |
| 51 | Correlation | RequestContextMiddleware validate incoming ASCII ID1–128; reuse hoặc GUID N; X-Request-ID response + structured RequestId logging scope. CORS expose Request-ID/Retry-After. Invalid ID test verifies replacement. |
| 52 | Logging | Rà soát không log auth headers/cookies/tokens/password/Google credentials/base64/raw bytes. Generic exception và storage/auth catches dùng error type/status/IDs/counts thay raw exception; avatar sync đổi email sang userId. EF sensitive-data logging không bật. Đây là source audit, không thay infrastructure log retention/redaction. |
| 53 | AutoMigrate | InitializeAsync: Development default true; ngoài Development default false. False chỉ check pending và fail-fast với thông báo apply separately, không migrate/seed. True migrate fail vẫn fail startup. appsettings.example false; appsettings.json thật không sửa. Design-time factory bắt buộc explicit MYLIFE_DESIGN_DATABASE, không start application/đọc app config. |
| 54 | Seed | Reuse idempotent roles ADMIN/USER và global generations với advisory lock. Không demo/sample default account. SeedAdmin chỉ tạo khi operator cung cấp valid explicit email/password; không overwrite/promote existing user. AutoMigrate=false không chạy seed; production bootstrap cần kế hoạch explicit nếu DB mới. Không seed FamilyTrees. |
| 55 | JWT tradeoff | Giữ active/current role DB check mỗi request để lock/demotion có hiệu lực ngay. Projection Id/IsActive/Roles giảm payload/tracking; verified ID reuse tránh controller lookup thứ hai. Không cache auth/delay revocation. Tương lai chỉ cache ngắn với invalidation/versioning sau đo tải. |
| 56 | Refresh retention | Audit: rotate/revoke còn giữ expired/revoked rows; có ExpiresAt/UserId indexes, chưa scheduled cleanup. Không xóa active sessions lượt này. Recommend bounded batches chỉ expired records sau grace/forensic retention 7–30 days, policy operator duyệt; không delete revoked nhưng chưa hết hạn nếu cần replay investigation. |
| 57 | Login logs | Chưa cleanup; recommend retention 90–180 days, giới hạn quyền truy cập/export và purge theo batch sau policy approved. Không tự xóa audit logs. |
| 58 | Headers | Thêm nosniff và strict-origin-when-cross-origin. Production HSTS/HTTPS hiện có giữ. Strict CSP, Permissions-Policy, frame-ancestors/X-Frame-Options cần report-only inventory Google Auth/Drive/fonts/embedding rồi mới enforce; chưa bật rộng gây regression. Static frontend headers cần cấu hình tại hosting/proxy, API middleware không thay static host. |
| 59 | Cookie/HTTPS | HttpOnly, Secure ngoài Development hoặc SameSiteNone, configurable SameSite(default Lax), HTTPS redirect/HSTS production giữ nguyên. Exact CORS+credentials, cookie mutation X-Requested-With guard giữ. Web tokens không localStorage/sessionStorage; native protocol không đổi. Sourceaudit + existing HTTP regressions; chưa production TLS deployment test. |
| 60 | ErrorBoundary | Reusable root và route-level keyed pathname; localized fallback+Try again reset, không stack/raw exception. Deliberate throw browser VI/EN và actual successful recovery PASS; nếu child vẫn lỗi sau retry fallback vẫn xuất hiện, không tự infinite retry. |
| 61 | Accessibility | Shared Modal dialog/aria-modal/title useId, initial focus không giật focus đang nhập, Tab cycling, restore focus, localizedclose/Escape hiện có. FamilyTree roving keyboardtabs/tabpanel, selected state; search/filterlabels; member/category/mindmap icon labels; gallery Enter/Space và aria-pressed filters; relationremove labels+accordion expanded; notification alertdialog, focus-visible CSS; loader status. Browser dialog checks PASS. Notification/lightbox vẫn bespoke, cần future assistive-tech audit; không claim WCAG certification. |
| 62 | Reduced motion | MotionConfig reducedMotion=user, CSS reduced transitions/animations, static LoadingScreen decorative paths, Mindmap không animated edges và cursor không mount. Node/browser regression PASS; không thay functionalcontrols. |
| 63 | Touch cursor | Wrapper media fine pointer+hover+no-preference; coarse/reduced không mount animation child/hooks; subscription handle preference changes. Browser mobiletouch+reduce thấy0 cursorelements. |
| 64 | Folder refactor | Chưa move toàn FamilyTree khỏi admin/components/services/types: nhiều imports/tests/i18n cũ nên optional risk vượt benefit lượt này. AdminDashboard được lazy riêng, không eager import FamilyTree. Không duplicate featurefolder. |
| 65 | Remaining debt | Full-tree payload và recursive graph DFS có giới hạn khi rất lớn; khi500–1000+ members cân nhắc paged search/generation/memberendpoint và graph endpoint riêng, projection/snapshot. Folder/admin.* naming, bespoke dialogs, data-driven Anniversary/Map (hiện static/demo UI), retention, edge limiter, headers và production operational verification tiếp tục audit sau. Không mở features mới. |
| 66 | Cross-user tests | FamilyIsolationTests4 PASS: same name ownership/private lists, ignored owner fields, bilateral GET/PUT/DELETE 404+unchanged data, foreign references all types POST/PUT 400, concurrent lazy creation: 1 row, cleanup giữ other tree. |
| 67 | Migration tests | FamilyTreeMigrationTests1 PASS: previousschema, four single role users/empty members, no automatic production update, Up/Down, NOT NULL/no-default/unique/restrict/indexes, Library columns unchanged, no pending model changes. Disposable schema riêng; không applicationmigration. |
| 68 | Role tests | Existing SystemFlows USER allowed, ADMIN403 all family/library actions, anonymous401, retiredalias404; frontend 5 role tests và browser Admin redirect/menu/không API PASS. Đã thay phần dual-role của yêu cầu cũ theo explicit XOR mới, không bỏ regressions cần thiết để pass. |
| 69 | Search tests | Actual helper/hook: fourfields, case/tones/trim, query examples, generationAND PASS trong8 optimizationtests. |
| 70 | Rate tests | WebRuntime four endpoint theory cases và per-user uploadcase PASS, overridden low limit, không sleep một phút, raw X-Forwarded-For không bypass, GET không bị ảnh hưởng; USER khác chưa hết limit. Existing suite test factory permits cao để tránh incidental throttling. |
| 71 | Health tests | Healthy isolated DB 200, DB unavailable: ready 503 / live 200, JSON chỉ status, request ID reuse/replacement/nosniff PASS. |
| 72 | Compression verify | GET privatefamily Accept-Encoding:gzip: Content-Encodinggzip, stream decompressed, success/data correct, no Age output cache PASS. Không báo br negotiation là tested. |
| 73 | Lazy evidence | Production .vite/manifest.json + static import closure assertion PASS. FamilyTree/Admin absent initial; Mindmap absent FamilyTree static closure and present dynamicImports. Browser Admin access guard confirmsno FamilyTree API. |
| 74 | Images | UI/harness JPG/PNG/WebP/GIF preview/upload/gallery/reload/lightbox/original/fallback PASS; browser gallery w640/lazy/async assertion. StorageNode42 và backend contracts verify formats/bytes/MIME/checksum under mocks. Không live Drive test/deploy ở lượt này. |
| 75 | Category CRUD | GET/POST/PUT/DELETE reuse, Du lịch→Du lịch gia đình slug du-lich stable, defaultkhông edit/delete, in-use không delete, unused delete. Backend+VI/EN Node+browser PASS; ownership/name-only DTO giữ. |
| 76 | Avatar regressions | Frontend12 PASS; backend Google/Drive/account/member contracts và storage Node 42 PASS. Manualavatar Google precedence, cache-busted accountlink, identityoverwrite/failure/delete behavior giữ. Member HTTP upload chưa tồn tại theo audit23. |
| 77 | Backend total | dotnet test Release:94 passed,0failed,0skipped trên isolated PostgreSQL. Auth/register/login/Google/refresh/logout/change-password/Admin/library/family/storage/migration regressions được giữ. |
| 78 | Frontend total | test:i18n11 + library4 + library-ui7 + roles5 + avatar12 + optimization8 =47 passed,0failed,0skipped. Browser end-to-end mocked API harness PASS riêng (17 groups); không cộng sốgroups vào Node count. Storage42 riêng không cộng vào backend 94. |
| 79 | Build/lint | Backend dotnet clean/restore/Releasebuild/test PASS. NU1900 warning: không fetch được nuget.org vulnerability feed trong network restricted; dependency vulnerability audit chưa xác nhận. EF tool 9.0.10/runtime 10.0.11 warning khi scaffold, generatedmodel/migration đã test. Frontend npm install/build/lint PASS, package-lock không đổi, không dependency mới. |
| 80 | Diffcheck | git diff --check PASS cả backend/frontend sau sửa trailing spaces; Mobile clean. Git chỉ warning LF→CRLF theo config Windows, không whitespace error. Untracked new source cũng kiểm tra EOF/whitespace riêng. |
| 81 | Apps Script | NO modifications/deploy. Cả ba .gs storage contracts chỉ test bằng mocked harness. |
| 82 | Mobile | NO modifications; git status/diff Mobile-Mylife clean. Không thêm Mobile calls hoặc đổi native token contract. |
| 83 | Application migration | NO. Chỉ scaffold/inspect và isolatedUp/Down. App audit READONLY vẫn history AddLibraryCategories,4 users/0 members. Không chạy startup mới vào appDB. |
| 84 | Git/deploy | NO commit/push/reset/restore/destructivecheckout/branchswitch/deploy. Local work giữ để review trên feature_minhduc. |
| 85 | Manual next steps | Review toàn diff/migration/test/report; backup/precondition check; explicit migration sau approval riêng; AutoMigrate=false trước restart; role/isolation/health/manualUX smoke sau deploy. Exact commands và sequence ở dưới. |

## Trạng thái local đã có trước lượt này

Backend tracked modified (9): API_CONTRACT.md; Features/Auth/Controllers/AccountController.cs; Features/FamilyTree/Controllers/FamilyTreeController.cs; Features/Library/Controllers/LibraryController.cs; Features/Library/Services/ILibraryService.cs; Features/Library/Services/LibraryService.Categories.cs; Tests/LibraryFlowsTests.cs; Tests/README.md; Tests/SystemFlowsTests.cs.

Backend untracked (2): FAMILYTREE_USER_CATEGORY_CRUD_REPORT.md; Features/Library/DTOs/UpdateLibraryCategoryDto.cs.

Frontend tracked modified (18): package.json; src/App.tsx; LibraryCategoryModal.tsx; LibraryTab.tsx; featureMessages.ts; libraryService.ts; ProtectedRoute.tsx; UserMenu.tsx; AuthContext.tsx; authService.ts; Header.tsx; en/admin.json; vi/admin.json; tests/LIBRARY_README.md; feature-i18n.test.cjs; library-ui.browser.cjs; library-ui.test.cjs; library.test.cjs. Components/services/locales giữ đúng paths hiện có dưới src/features hoặc src/shared.

Frontend untracked (1): tests/family-role.test.cjs. Không undo local changes này; thay semantics role bằng yêu cầu XOR hiện tại. Report lịch sử/i18n trước đó giữ nguyên.

## Production readiness checklist

Các dấu x dưới đây là code/source/test đã xác minh trong local/isolated environment, không khẳng định đã rollout production. Schemachecks mới PASS trên disposable DB; application DB vẫn chưa có new migration.

- [x] ADMIN XOR USER model respected
- [x] FamilyTree per USER (lazy unique owner)
- [x] family_tree_id NOT NULL, no default 0 (isolated migration)
- [x] Cross-user isolation
- [x] Cross-tree relationship inputs blocked
- [x] FamilyTree indexes
- [x] Query optimization
- [x] Search enhanced
- [x] No fake loading
- [x] FamilyTree lazy
- [x] Admin lazy
- [x] Mindmap lazy
- [x] Image optimization
- [x] Rate limiting (single instance built-in)
- [x] Upload limits (application rules; proxy limit still deployment check)
- [x] Response compression (gzip tested, Brotli registered)
- [x] /health/live
- [x] /health/ready
- [x] X-Request-ID
- [x] No secrets logged (source audit; test outputs không credentials)
- [x] Production migration strategy (pending fails; explicit apply)
- [x] ErrorBoundary
- [x] Empty/error/retry states
- [x] Accessibility improvements / targeted browserchecks
- [x] Reduced motion
- [x] Secure cookies (source + existing regressions)
- [x] Exact CORS
- [x] Existing regression tests
- [x] Backend build Release
- [x] Frontend production build
- [x] Lint
- [x] git diff --check
- [ ] Application DB AddPerUserFamilyTrees applied — intentionally pending review
- [ ] Production deployment/TLS/proxy/monitoring verification — not performed
- [ ] Live Drive regression — not performed; all remote tests mocked
- [ ] NuGet vulnerability feed audit — blocked by networkavailability, not a buildfailure

## Lệnh kiểm tra và artifacts

Đã chạy clean/restore/build Release. Restore dùng temporary NuGet config trỏ local packagecache vì user NuGet config không đọc được trong sandbox; không thay project/package configuration. Tests dùng PostgreSQL disposable localhost:55439, database mylife_web_validation/user mylife_test, unique legacy_*/tree_migration_* schemas. Logging EventLog disabled chỉ test process. Không dùng application connectionstring cho tests.

Lệnh tương đương khi người review chạy ở terminal bình thường (MYLIFE_TEST_DATABASE phải là disposable DB):

~~~powershell
cd D:\DUAN\MYLIFE\Backend-MyLife
dotnet clean -c Release
dotnet restore Tests/Backend-MyLife.Tests.csproj
dotnet build -c Release --no-restore
$env:Logging__EventLog__LogLevel__Default = 'None'
# Set MYLIFE_TEST_DATABASE from a disposable database provisioned for testing.
dotnet test Tests/Backend-MyLife.Tests.csproj -c Release --no-restore
node --test Tests/apps-script-avatar.test.cjs Tests/apps-script-member-avatar.test.cjs Tests/apps-script-library.test.cjs

cd D:\DUAN\MYLIFE\Frontend-MyLife
npm install
npm run build
npm run lint
npm run test:i18n
npm run test:library
npm run test:library-ui
npm run test:roles
npm run test:avatar
npm run test:optimization
~~~

Browser: start Vite 127.0.0.1:7001; set BROWSER_EXECUTABLE to installed Chromium; node tests/library-ui.browser.cjs. Restricted harness launch used optional LIBRARY_UI_NO_SANDBOX=1, tests only; normal browser harness default sandbox. Every /api request intercepted, no real backend/Drive writes.

Logs/before-diff temporary directory: C:/Users/admin/AppData/Local/Temp/mylife-web-audit-88wPS3. Browser screenshots/checklog artifacts: C:/Users/admin/AppData/Local/Temp/mylife-library-browser-f5BFM4. Reviewed mobile gallery/lightbox screenshots; mobile 390px and dark mode regression PASS. These temp artifacts may be removed by OS; source test files are reproducible evidence.

## Manual steps sau review (chưa thực hiện)

1. Review git diff và tất cả file mới/untracked trong hai repos. Tập trung FamilyTreeService, AppDbContext, migration/Designer/snapshot, middleware và tests. Không restart backend mới trước khi review migration: Development mặc định AutoMigrate=true.
2. Backup application DB, xác nhận family_members vẫn empty và mỗi account có đúng một ADMIN hoặc USER. Tạm ngừng writes trong cửa sổ migration. Nếu xuất hiện members mới, dừng để thiết kế ownership migration riêng; guard hiện tại sẽ từ chối, không tự backfill.
3. Đặt Database__AutoMigrate=false cho runtime kể cả local khi dùng migration thủ công. Cấp MYLIFE_DESIGN_DATABASE cho EF design factory từ secret manager hoặc local environment an toàn; không ghi credentials vào source/report/log. Factory không đọc appsettings và không khởi động host.
4. Generate SQL để review; command này tạo file SQL, không apply:

~~~powershell
cd D:\DUAN\MYLIFE\Backend-MyLife
# Supply MYLIFE_DESIGN_DATABASE securely; do not print its value.
dotnet ef migrations script 20261006090341_AddLibraryCategories 20261007080251_AddPerUserFamilyTrees --configuration Release --output AddPerUserFamilyTrees.review.sql
~~~

5. Sau khi operator duyệt riêng, apply SQL đã review qua migration pipeline hoặc EF command dưới đây. Đây là hướng dẫn tương lai; agent không chạy command này vào application DB.

~~~powershell
dotnet ef database update 20261007080251_AddPerUserFamilyTrees --configuration Release
Remove-Item Env:MYLIFE_DESIGN_DATABASE
~~~

6. Kiểm tra migration history, member column NOT NULL/không default, FKs RESTRICT, unique owner và indexes; không seeded trees hay account changes. DB mới cần bootstrap roles/generations/optional admin có kiểm soát, vì production AutoMigrate=false không seed. DB hiện tại giữ roles của bốn accounts.
7. Start Release backend đã review với AutoMigrate=false, rồi rollout frontend theo workflow người dùng. Kiểm tra live/ready, gzip JSON, X-Request-ID, exact CORS, credentials, HTTPS cookies và reverse proxy body limit tối thiểu 101 MiB. Nếu dùng proxy, chỉ cấu hình trusted proxies thực tế trước khi tin forwarded IP. Multi-instance cần edge/shared limiter riêng.
8. Dùng hai USER sessions: tạo A/B members cùng tên; mỗi list chỉ chứa own members; foreign GET/PUT/DELETE trả404, foreign references trả400, dữ liệu owner không đổi. ADMIN dashboard hoạt động, family/library trả403 và không có navigation. Smoke auth, album/category/photo CRUD, rename giữ slug du-lich, thumbnail/reload/lightbox/original link và account avatar.
9. Kiểm tra VI/EN, dark/light, mobile, keyboard tabs, modal Escape/focus return, empty/error/retry và Mindmap TB/LR/MiniMap/export PNG. Anniversary/Map giữ UI hiện có; chuyển thành data-driven vẫn là technical debt. Không chạy live Drive tests trước khi authorize remote test writes.
10. Chốt policy retention/logs, headers report-only và trusted proxy/edge limits trước khi tăng production scale. Không rollback Down với dữ liệu mới nếu chưa backup và có kế hoạch phục hồi ownership. Dừng để review: không commit/push/deploy từ lượt này.
