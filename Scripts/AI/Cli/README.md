# PSD2UIForm 专用 CLI

此目录编译进主体 `cn.efunstudio.psd2ugui` 程序集，没有独立 CLI 程序集，也不依赖 AIUIForm。支持 Claude、Codex、OpenCode 和 Qoder，只执行 PSD 的视觉识别与局部结构修正，不包含生图、背景移除或模型服务。

识别模式保存在 PSD2UIForm 的 `aiProviderConfig.mode`，直接在主体 Inspector 中选择。CLI 的账号、模型和推理参数由 CLI 自身管理，没有额外的 AI 设置窗口。

识别输入由 CLI 从任务目录读取 `manifest.json`、`nodes.tsv`，不再把这些文件内容重复拼入提示词。调用保存请求、提示词、参考图、原始事件、状态及识别协议验证结果，不生成或读取 JSON Schema 文件。只播报一次调用阶段，不输出定时心跳或重试进度；有限结构修正、缓存重新校验和取消仍执行。进程归属通过 PID、启动时间与可执行文件校验，取消只终止本次拥有的进程。

每轮识别在 `Library/Psd2UIForm/<PSD作用域>` 重新初始化日志和状态，调用证据位于该任务的 `.ai-calls`。CLI 子进程不继承 HTTP_PROXY、HTTPS_PROXY、ALL_PROXY，保留 CLI 自身账号、模型及其他配置。
