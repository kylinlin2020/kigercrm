# 数据库升级脚本

由 `UpgradeEngine` 执行，执行台账记入 `sys_migration_history`。

## 执行时机

数据库初始化分三段：**全部连接建库建表 → 升级脚本 → 播种**（`SaasSchemaUpgrader` 接在框架 `IDbSchemaUpgrader` 上）。

- 建表只建缺失的表、**不改已存在的表**，存量表的新列、新索引只能由脚本补。
- 脚本先于种子执行：种子按最新实体读写，存量表的新列要在种子之前补齐，否则种子一查就撞上不存在的列。
- **新库不跑脚本**：本次启动从零建出全部实体表的平台库、`InitializeDatabase` 新建的库隔离租户独立库，建好即登记为最新脚本版本（`IUpgradeEngine.BaselineAsync`）。
  脚本只在它所属版本之前建的库上执行，按当时的表结构写即可；**不要在脚本里依赖种子数据**。
- 关闭 `XiHan:Upgrade:EnableAutoCheckOnStartup` 时启动不执行脚本，须先手工升级再启动。

## 约定

- **默认只提供PostgreSQL，其他数据库需自行实现**
- **目录名即版本号**：脚本放在 `UpdateScripts/<版本>/` 下，如 `3.10.0/3.10.0.sql`。
  框架的 `FileSystemUpgradeScriptProvider` 只扫**子目录**（`Directory.GetDirectories`），
  同一版本目录内可以放多个 .sql，按文件名升序执行。
- **只有版本号高于库中 `db_version` 的脚本会执行**（记在 `sys_version`，随脚本执行推进）。
  与程序版本 `props/version.props` **无关**——`UpgradeEngine.ExecuteMigrationsAsync` 只比库版本，
  不比较 AppVersion。
- **每个库各跑一遍**：平台库，加上每个已配置完成的库隔离租户的独立库（`SaasUpgradeTenantProvider`）。
  新建的独立库同样登记为最新版本，之后发版的脚本会在它上面执行。
- **独立库只有租户库实体的表**：平台目录、账号与授权、会话令牌、读共享模板等标了 `[PlatformDataSource]` 的实体只在平台库建表。
  改这些表的语句必须先判表存在，否则在独立库上报 `42P01`、中断启动：

  ```sql
  DO $$
  BEGIN
      IF EXISTS (SELECT 1 FROM information_schema.tables
                  WHERE table_schema = current_schema() AND table_name = 'sys_user') THEN
          -- 改 sys_user 的语句
      END IF;
  END
  $$;
  ```
- **公共列一律带下划线**：`basic_id`、`tenant_id`、`created_time`、`is_deleted` 等，所有实体基类（含聚合根）同名；
  5.3.0 之前聚合根的公共列没有声明列名、落库成 `basicid` / `tenantid`，5.3.0 已把存量库改名。
- **标识符一律小写、不加引号。** SqlSugar 建表时未加引号，PostgreSQL 将未加引号的标识符折叠为小写，
  所以库里的实际名是 `sys_oauth_code`、`basic_id`，而不是实体上声明的 `Sys_OAuth_Code`、`Basic_Id`。
  写成 `"Sys_OAuth_Code"` 会因引号带来大小写敏感而报 `42P01 relation does not exist`。
- **写成可重复执行**：用 `IF NOT EXISTS` / `IF EXISTS`。失败的脚本不会记为已执行，下次启动会重试。
- **PostgreSQL 方言**：执行器在 PG 上取事务级建议锁并把本次全部脚本放进同一事务，失败整体回滚。
- CRM Lead 重复识别的规范化查询键从 5.9.2 起由 `5.9.2/5.9.2.sql` 增加并回填；脚本只创建普通索引，不设置业务唯一约束。
- CRM Contact 主联系人唯一性从 5.9.3 起由 `5.9.3/5.9.3.sql` 增加 `primary_customer_id` 并建立 `(tenant_id, primary_customer_id)` 唯一索引；非主联系人该列为 NULL，允许同一客户有多个普通联系人。迁移遇到存量重复主联系人会明确失败并中断启动，需先清理重复数据。

- CRM 自动获客 Phase A 持久化基础从 5.9.11 起增加 `crm_prospecting_campaign`、`crm_prospecting_strategy`、`crm_prospecting_run` 三张租户级表；仅在存在 CRM Lead 表的租户库执行，平台库跳过。Campaign/Strategy 为完整审计实体，策略版本按租户 + Campaign + Version + 删除状态唯一；Run 是永久创建审计记录（不含软删除列），按租户 + RequestKey 永久唯一，避免删除历史任务释放幂等键。新库由 CRM 实体 CodeFirst 创建；本迁移用于既有 CRM 租户库。

## 失败会怎样

脚本抛错即整体回滚、写入一条 `Success = false` 的台账，并**中断应用启动** —— 宁可起不来，
也不让应用带着半吊子表结构对外服务。修好脚本后重启即可，失败记录不影响重试。

- CRM 销售订单来源报价及报价明细唯一性从 5.9.4 起由 `5.9.4/5.9.4.sql` 为存量库补齐；迁移前会检查重复的活动来源关联，发现冲突即中止并要求先清理数据。
- CRM 报价版本号唯一性（租户 + 报价编号 + 版本 + 删除状态）、销售订单来源报价唯一性及订单内来源报价明细唯一性从 5.9.4 起由 `5.9.4/5.9.4.sql` 为存量库补齐；迁移前按完整唯一键检查重复记录，发现冲突即中止并要求先清理数据。
- CRM 销售订单明细发货数量台账从 5.9.5 起由 `5.9.5/5.9.5.sql` 增加 `reserved_quantity` 与 `shipped_quantity`，并约束两者非负且合计不得超过订单数量。草稿发货预占、取消释放、实际发货转记均在事务内执行；订单明细 RowVersion 用于并发冲突检测。
- CRM 销售订单号与发货单号的租户级唯一索引从 5.9.6 起由 `5.9.6/5.9.6.sql` 为存量库补齐；迁移前检查包括软删除记录在内的完整唯一键，发现重复即中止并要求先清理。新库由实体 CodeFirst 索引创建。
- CRM 报价、销售订单、发货单编号自 5.9.7 起按租户永久唯一；唯一键不包含软删除标记，软删除不会释放编号。迁移会先检查所有历史记录（含软删除）是否存在重复编号/版本，存在时中止并要求人工处理。
- CRM 发货单创建请求指纹从 5.9.8 起由 `5.9.8/5.9.8.sql` 增加；使用服务端编号幂等键创建发货单时，持久化规范化请求的 SHA-256 指纹，顺序重试按租户内发货单号定位原单并校验指纹，避免重复预占订单数量；并发首次请求仍须通过 PostgreSQL 集成测试验证唯一键冲突后的事务回滚与客户端重试行为。历史发货单指纹为 NULL，不会被当作可安全重放的请求。
- CRM 直接创建销售订单的请求指纹从 5.9.9 起由 `5.9.9/5.9.9.sql` 增加；使用服务端编号幂等键时，顺序重试按租户内订单号定位原单并校验规范化请求的 SHA-256 指纹，匹配则返回原订单，避免重复创建订单头与明细；并发首次请求仍须通过 PostgreSQL 集成测试验证唯一键冲突后的事务回滚与客户端重试行为。历史订单指纹为 NULL，不会被当作可安全重放的请求。
- CRM 销售订单与发货单创建请求永久幂等账本从 5.9.10 起由 `5.9.10/5.9.10.sql` 创建；仅在存在 CRM 销售订单或发货单表的租户数据库执行建表与索引，平台库跳过。唯一键为租户 + 单据类型 + 请求键，账本不含软删除字段；相同请求键重放时校验 SHA-256 指纹并返回原单，不同请求内容拒绝。

- CRM 自动获客 Run 请求指纹从 5.9.12 起由 `5.9.12/5.9.12.sql` 为既有租户库增加 `payload_hash`；新库由实体 CodeFirst 创建。启动 Run 时绑定 Campaign 当前已激活且已审批的策略版本，租户内 RequestKey 永久唯一；相同 key + 相同策略快照返回既有 Run，不同快照拒绝。历史 Run 的空指纹不视为可安全重放。

- CRM 自动获客 Run 的原始 Provider 结果快照从 5.9.13 起由 `5.9.13/5.9.13.sql` 为既有 CRM 租户库增加可空 `raw_results_json`（PostgreSQL text）；新库由 Run 实体 CodeFirst 创建。只有 Worker 完成 Provider 调用后才写入该字段，候选数仍为 0，原始结果不等于已审核线索。

- CRM 自动获客候选审核表从 5.9.14 起由 `5.9.14/5.9.14.sql` 为已有 CRM 租户库创建（要求 `crm_lead` 与 `crm_prospecting_run` 已存在）；新库由 `SysCrmProspectingCandidate` CodeFirst 创建。候选与 CRM Lead 分表保存，必须经人工审核后才允许转为 Lead。

- CRM 自动获客 Run 从 5.9.15 起保存启动时的完整执行输入快照（搜索目标、产品范围、目标市场、客户画像、Provider 计划及排除域名）；Worker 只读取 Run 快照，不再依赖可变 Campaign/Strategy 当前值。既有 Run 不会被伪造回填为历史快照；旧的未完成 Run 若需恢复，应在升级前完成或由运维核查后重新发起。

- CRM 自动获客候选状态迁移 5.9.16：引入内部 Converting 状态后，旧版本 Converted 数值 4 必须迁移为 5；升级脚本仅在候选表存在时执行，避免平台库引用 CRM 表。升级前应停止旧版本应用，避免新旧枚举代码并行写入。

- CRM 自动获客评分证据追溯从 5.9.23 起增加 `evidence_references_json`，在每条不可变 Score 快照中保存本次评分所依据的 Provider 记录标识、字段名、来源 URL 指纹、Payload 指纹及证据类型/核验状态；按稳定键排序，最多 100 条且序列化不超过 32 KB。历史评分仅回填空数组，不推测历史证据关联。新库由 Score 实体 CodeFirst 创建，既有租户库由 `5.9.23/5.9.23.sql` 补列。
- CRM 自动获客 Evidence 幂等身份从 5.9.21 起由 `5.9.21/5.9.21.sql` 为既有租户库规范化 `provider_record_id`、`raw_payload_hash` 的 NULL 值，并增加 `source_url_hash`（SHA-256 URL 指纹），建立租户 + Candidate + Provider + Provider Record + 字段 + URL 指纹 + Payload Hash 普通复合唯一索引。Evidence 实体同步声明等价 CodeFirst 唯一索引，因此新租户与升级租户走同一身份结构；完整 URL 仍保留在 `source_url` 用于审阅，索引不再直接包含最长 2000 字符的 URL。迁移使用 PostgreSQL `pgcrypto` 扩展计算 SHA-256；数据库账号需具备该扩展的安装权限，或由管理员预先安装。迁移先检查历史重复身份，发现冲突即中止，不自动删除证据。

- CRM 自动获客 Provider 请求尝试台账从 5.9.31 起由 `5.9.31/5.9.31.sql` 创建 `crm_prospecting_provider_request`，仅在存在 `crm_prospecting_run` 的租户 CRM 数据库执行。台账记录每次 Provider 调用的租户、Run、Provider、尝试序号、请求 SHA-256 指纹、状态、时间、结果数量与受限诊断；不存储原始响应、HTTP 认证头或凭据。尝试身份按租户 + Run + Provider + Attempt_No 唯一，状态与计数由数据库约束保护。仓储已注册并接入 Worker：调用前持久化 Started，调用后条件更新终态；并发尝试编号冲突会重新读取最大编号并有限重试。失败诊断需清理 URL 查询参数、Bearer/API key 等凭据。仍待完成 PostgreSQL 并发验收、崩溃后 Started 恢复及完整运行验收。
- CRM 自动获客 Provider 请求台账运行时写入从 5.9.31 开始接入：`ICrmProspectingProviderRequestRepository` 在 Worker 发起网络请求前写入 `Started`，结束后条件更新为 `Succeeded`、`Failed`、`RateLimited` 或 `Cancelled`；尝试编号按租户 + Run + Provider 递增，唯一索引作为并发竞争最终仲裁。写入/终态更新失败会阻止 Worker 继续把该 Provider 调用当作正常结果处理。进程崩溃遗留 `Started` 的恢复、PostgreSQL 双连接并发验收与运维查询仍待完成。
- Provider 请求台账的崩溃遗留恢复也纳入 Worker 启动流程：Run 过期租约恢复后，超过 15 分钟且所属 Run 已不再持有有效 Running/CancelRequested 租约的 Started 尝试会被标记为 `Failed/worker_interrupted`；活跃租约下的请求不会被恢复逻辑抢先终结。恢复 SQL 使用 PostgreSQL 数据库时钟并严格限定租户。需在 PostgreSQL 实机环境验证该恢复 SQL 与双连接竞争行为。
