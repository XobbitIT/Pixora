# Pixora — інструкції для Claude Code

Постійні правила проєкту спільні для всіх агентів і лежать в AGENTS.md (його автоматично читає Codex). Claude Code читає цей файл, тому правила імпортуються звідси:

@AGENTS.md

Поточний стан — у PROJECT_STATE.md (читати перед роботою, як вимагає AGENTS.md).

Під час запуску через AI Team Console (`race`/`run`) працюєш у тимчасовому Git worktree: не комітай, не змінюй PROJECT_STATE.md і AgentExchange/ — обраний результат запише людина.
