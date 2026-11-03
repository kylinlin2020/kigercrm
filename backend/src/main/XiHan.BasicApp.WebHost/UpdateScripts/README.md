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

## 失败会怎样

脚本抛错即整体回滚、写入一条 `Success = false` 的台账，并**中断应用启动** —— 宁可起不来，
也不让应用带着半吊子表结构对外服务。修好脚本后重启即可，失败记录不影响重试。

- CRM 销售订单来源报价及报价明细唯一性从 5.9.4 起由 `5.9.4/5.9.4.sql` 为存量库补齐；迁移前会检查重复的活动来源关联，发现冲突即中止并要求先清理数据。
- CRM 报价版本号唯一性（租户 + 报价编号 + 版本 + 删除状态）、销售订单来源报价唯一性及订单内来源报价明细唯一性从 5.9.4 起由 `5.9.4/5.9.4.sql` 为存量库补齐；迁移前按完整唯一键检查重复记录，发现冲突即中止并要求先清理数据。
- CRM 销售订单明细发货数量台账从 5.9.5 起由 `5.9.5/5.9.5.sql` 增加 `reserved_quantity` 与 `shipped_quantity`，并约束两者非负且合计不得超过订单数量。草稿发货预占、取消释放、实际发货转记均在事务内执行；订单明细 RowVersion 用于并发冲突检测。
- CRM 销售订单号与发货单号的租户级唯一索引从 5.9.6 起由 `5.9.6/5.9.6.sql` 为存量库补齐；迁移前检查包括软删除记录在内的完整唯一键，发现重复即中止并要求先清理。新库由实体 CodeFirst 索引创建。\n