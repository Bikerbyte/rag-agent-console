(() => {
    const form = document.getElementById("agentComposeForm");
    const thread = document.getElementById("agentThread");
    const historyInput = document.getElementById("agentHistoryJson");
    if (!form || !thread || !historyInput) {
        return;
    }

    const messageInput = form.querySelector("textarea[name='AgentInput.Message']");
    const sendButton = form.querySelector("button[type='submit']");

    const escapeHtml = (value) => value
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;")
        .replaceAll("'", "&#039;");

    const renderInlineMarkdown = (value) => {
        let html = escapeHtml(value);
        html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
        html = html.replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>');
        return html;
    };

    const renderMarkdown = (value) => {
        const lines = value.replace(/\r\n/g, "\n").split("\n");
        const blocks = [];
        let paragraph = [];
        let list = [];

        const flushParagraph = () => {
            if (paragraph.length === 0) {
                return;
            }

            blocks.push(`<p>${renderInlineMarkdown(paragraph.join(" "))}</p>`);
            paragraph = [];
        };

        const flushList = () => {
            if (list.length === 0) {
                return;
            }

            blocks.push(`<ul>${list.map((item) => `<li>${renderInlineMarkdown(item)}</li>`).join("")}</ul>`);
            list = [];
        };

        for (const line of lines) {
            const trimmed = line.trim();
            if (!trimmed) {
                flushParagraph();
                flushList();
                continue;
            }

            const listMatch = trimmed.match(/^(\d+\.\s+|[-*]\s+)(.+)$/);
            if (listMatch) {
                flushParagraph();
                list.push(listMatch[2]);
                continue;
            }

            flushList();
            paragraph.push(trimmed);
        }

        flushParagraph();
        flushList();
        return blocks.join("");
    };

    const hydrateMarkdown = (root = document) => {
        root.querySelectorAll(".agent-markdown[data-markdown]").forEach((element) => {
            element.innerHTML = renderMarkdown(element.dataset.markdown || "");
            element.removeAttribute("data-markdown");
        });
    };

    const renderTrace = (trace) => {
        if (!trace) {
            return "";
        }

        const matches = (trace.matches || []).slice(0, 5).map((match) => `
            <div class="trace-match-row">
                <span>#${match.rank}</span>
                <strong>${escapeHtml(match.label || "-")}</strong>
                <small>${escapeHtml(match.moduleName || "-")} · ${escapeHtml(match.sourceKind || "-")} · score ${(match.score || 0).toFixed(3)}</small>
            </div>`).join("");

        return `
            <details class="agent-trace">
                <summary>Retrieval trace</summary>
                <dl>
                    <div><dt>Intent</dt><dd>${escapeHtml(trace.intent || "-")}</dd></div>
                    <div><dt>Module</dt><dd>${escapeHtml(trace.moduleName || "-")}</dd></div>
                    <div><dt>Query</dt><dd>${escapeHtml(trace.retrievalQuery || "-")}</dd></div>
                    <div><dt>Mode</dt><dd>${escapeHtml(trace.retrievalMode || "-")}</dd></div>
                </dl>
                ${matches ? `<div class="trace-match-list">${matches}</div>` : ""}
            </details>`;
    };

    const createMessage = (role, content, options = {}) => {
        const empty = thread.querySelector("[data-agent-empty]");
        if (empty) {
            empty.remove();
        }

        const wrapper = document.createElement("div");
        wrapper.className = `chat-message ${role === "user" ? "is-user" : "is-agent"}${options.thinking ? " is-thinking" : ""}`;

        const label = document.createElement("span");
        label.textContent = role === "user" ? "You" : "Agent";

        const bubble = document.createElement("div");
        bubble.className = "chat-bubble agent-markdown";
        if (options.thinking) {
            bubble.innerHTML = '<span class="thinking-dots"><i></i><i></i><i></i></span><em>Thinking</em>';
        } else {
            bubble.innerHTML = renderMarkdown(content);
        }

        wrapper.append(label, bubble);
        if (!options.thinking && role !== "user") {
            wrapper.insertAdjacentHTML("beforeend", renderTrace(options.trace));
        }
        thread.append(wrapper);
        thread.scrollTop = thread.scrollHeight;
        return wrapper;
    };

    const setBusy = (isBusy) => {
        form.classList.toggle("is-busy", isBusy);
        if (sendButton) {
            sendButton.disabled = isBusy;
            sendButton.textContent = isBusy ? "Thinking" : "Send";
        }
        if (messageInput) {
            messageInput.disabled = isBusy;
        }
    };

    hydrateMarkdown();
    thread.scrollTop = thread.scrollHeight;

    document.querySelectorAll(".agent-starter-chip").forEach((chip) => {
        chip.addEventListener("click", () => {
            if (messageInput) {
                messageInput.value = chip.dataset.starterText || chip.textContent;
                messageInput.focus();
            }
        });
    });

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const message = messageInput?.value.trim();
        if (!message) {
            return;
        }

        createMessage("user", message);
        const thinking = createMessage("assistant", "", { thinking: true });
        const thinkingStartedAt = Date.now();
        messageInput.value = "";
        setBusy(true);

        try {
            const formData = new FormData(form);
            formData.set("AgentInput.Message", message);

            const response = await fetch("?handler=AskAgentJson", {
                method: "POST",
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                },
                body: formData
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const payload = await response.json();
            historyInput.value = payload.historyJson || "";
            const remainingThinkingTime = Math.max(0, 650 - (Date.now() - thinkingStartedAt));
            if (remainingThinkingTime > 0) {
                await new Promise((resolve) => setTimeout(resolve, remainingThinkingTime));
            }
            thinking.remove();

            for (const item of payload.newMessages || []) {
                if (item.role === "assistant") {
                    createMessage("assistant", item.content || "", { trace: item.trace });
                }
            }
        } catch {
            thinking.classList.remove("is-thinking");
            thinking.querySelector(".chat-bubble").textContent = "Agent request failed. Please try again.";
        } finally {
            setBusy(false);
            messageInput?.focus();
        }
    });
})();
