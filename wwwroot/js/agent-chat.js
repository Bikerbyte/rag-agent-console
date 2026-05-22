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

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const message = messageInput?.value.trim();
        if (!message) {
            return;
        }

        createMessage("user", message);
        const thinking = createMessage("assistant", "", { thinking: true });
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
            thinking.remove();

            for (const item of payload.newMessages || []) {
                if (item.role === "assistant") {
                    createMessage("assistant", item.content || "");
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
