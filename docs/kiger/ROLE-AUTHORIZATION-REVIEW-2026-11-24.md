# 角色授权评审与权限码迁移（2026-11-24）

## 1. 背景

安全审查发现两类权限问题，需先评审角色授权再决定是否/如何迁移：

- **A. 知识类服务权限码错用**：`KnowledgeAccessAppService` / `KnowledgeTagAppService` / `KnowledgeReviewAppService`
  用 `ai:read` / `ai:update` / `ai:execute`（AI provider 资源），而同模块 `KnowledgeDocumentAppService` /
  `KnowledgeQueryAppService` 用 `knowledge_base:*`（知识库资源）。前端 `PageRegistry` 知识库按钮挂的是
  `knowledge_base:*`，前后端不一致。
- **B. CRM `crm:execute` 一码多用**：提交、审批、取消运行、候选人审核、签发证书等共用同一码。

## 2. 角色授权现状（评审依据）

`SaasDemoScenario.cs` 的演示角色授权：

| 角色 | 授权方式 | 是否显式列 ai:/knowledge_base: 码 | 迁移影响 |
|---|---|---|---|
| `tenant_admin` | `DemoGrant.All`（全部权限） | 否（全部覆盖） | 无：新旧码都自动拥有 |
| `viewer` | `DemoGrant.AllRead`（所有 `:read` 结尾） | 否（按后缀匹配） | 无：`knowledge_base:read` 仍匹配 |
| `employee` / `dept_manager` / `hr_specialist` / `project_lead` / `archived` | 明确 codes | 否 | 无：本来就没有这些码 |
| `platform_operator` / `global_auditor` | 明确 codes | 否 | 无 |

**结论**：内置演示角色均不显式依赖 `ai:read/update/execute` 来访问知识管理，A 迁移对内置角色**零影响**。

**生产环境注意**：若客户自建角色显式授予了 `ai:*` 以使用知识库 ACL/标签/审核，迁移后这些角色将失去该能力，
需管理员改为授予 `knowledge_base:*`。这是**权限收紧的正确方向**，但需在升级说明中告知。

权限目录 `AiPermissionCatalogSeeder` 已同时登记 `Ai` 与 `Knowledge` 两套资源的操作码，
`KnowledgePermissionCodes` 与种子一一对应，迁移不需要改种子。

## 3. 迁移决策

### A. 知识类服务：`ai:*` → `KnowledgePermissionCodes.*`（执行）

| 服务 | 操作 | 原码 | 新码 |
|---|---|---|---|
| KnowledgeAccessAppService | 更新 ACL | `ai:update` | `knowledge_base:update` |
| KnowledgeAccessAppService | 读取 ACL | `ai:read` | `knowledge_base:read` |
| KnowledgeTagAppService | 写标签 | `ai:update` | `knowledge_base:update` |
| KnowledgeTagAppService | 读标签/重复报告 | `ai:read` | `knowledge_base:read` |
| KnowledgeReviewAppService | 提交/裁决审核 | `ai:execute` | `knowledge_base:execute` |
| KnowledgeReviewAppService | 读审核意见 | `ai:read` | `knowledge_base:read` |

同步更新源码断言测试（`KnowledgeAccessTests` / `KnowledgeReviewTests` / `KnowledgeTagTests`）。

### B. CRM `crm:execute` 拆分（延后）

拆分需新增权限码、改种子、改前端按钮、改角色授权，且 `crm:execute` 在多个业务动作中语义已稳定。
本次**不拆分**，仅记录为后续独立变更；避免把权限模型变更与本次安全修复耦合。

## 4. 验证口径

- 构建 + 全量测试 0 失败；
- 内置演示角色授权语义不变（评审已确认）；
- 升级说明提示：自建角色若依赖 `ai:*` 做知识管理，需改授 `knowledge_base:*`。
