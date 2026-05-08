function escapeHtml(value) {
  return `${value ?? ""}`
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function countMatches(text, regex) {
  if (!text) {
    return 0;
  }
  const matches = text.match(regex);
  return matches ? matches.length : 0;
}

function canonicalizeMarkdownTableRow(line) {
  const trimmed = `${line ?? ""}`.trim();
  if (!trimmed.includes("|")) {
    return "";
  }
  if (/^https?:\/\//i.test(trimmed)) {
    return "";
  }

  let candidate = trimmed;
  if (!candidate.startsWith("|")) {
    candidate = `| ${candidate}`;
  }
  if (!candidate.endsWith("|")) {
    candidate = `${candidate} |`;
  }

  const cells = candidate
    .slice(1, -1)
    .split("|")
    .map((cell) => `${cell ?? ""}`.trim());
  if (cells.length < 2 || cells.every((cell) => !cell)) {
    return "";
  }

  return `| ${cells.join(" | ")} |`;
}

function canonicalizeMarkdownTableSeparatorLine(line, expectedCells = 0) {
  const dashVariantsRegex = /[\u2014\u2013\u2011\u2212\u2500\u2012]/g;
  const normalizedRow = canonicalizeMarkdownTableRow(line);
  if (!normalizedRow) {
    return "";
  }

  const rawCells = normalizedRow
    .slice(1, -1)
    .split("|")
    .map((cell) => `${cell ?? ""}`.trim());
  if (rawCells.length < 2) {
    return "";
  }
  if (expectedCells > 0 && rawCells.length !== expectedCells) {
    return "";
  }

  const normalizedCells = [];
  for (const cell of rawCells) {
    const compact = `${cell ?? ""}`.replace(/\s+/g, "").replace(dashVariantsRegex, "-");
    if (!/^:?-+:?$/.test(compact)) {
      return "";
    }

    const leadingColon = compact.startsWith(":") ? ":" : "";
    const trailingColon = compact.endsWith(":") ? ":" : "";
    const dashCount = Math.max(3, countMatches(compact, /-/g));
    normalizedCells.push(`${leadingColon}${"-".repeat(dashCount)}${trailingColon}`);
  }

  return `| ${normalizedCells.join(" | ")} |`;
}

function normalizeMarkdownTableBlocks(text) {
  if (!text) {
    return "";
  }

  const lines = `${text ?? ""}`.split("\n");
  let changed = false;

  for (let i = 0; i + 1 < lines.length; i += 1) {
    const headerRow = canonicalizeMarkdownTableRow(lines[i]);
    if (!headerRow) {
      continue;
    }

    const headerCells = headerRow
      .slice(1, -1)
      .split("|")
      .map((cell) => `${cell ?? ""}`.trim());
    const separatorRow = canonicalizeMarkdownTableSeparatorLine(lines[i + 1], headerCells.length);
    if (!separatorRow) {
      continue;
    }

    if (lines[i] !== headerRow) {
      lines[i] = headerRow;
      changed = true;
    }
    if (lines[i + 1] !== separatorRow) {
      lines[i + 1] = separatorRow;
      changed = true;
    }

    for (let j = i + 2; j < lines.length; j += 1) {
      const bodyRow = canonicalizeMarkdownTableRow(lines[j]);
      if (!bodyRow) {
        break;
      }

      if (lines[j] !== bodyRow) {
        lines[j] = bodyRow;
        changed = true;
      }
    }
  }

  return changed ? lines.join("\n") : text;
}

function hasMarkdownTableBlock(text) {
  const lines = `${text ?? ""}`.split("\n");
  for (let i = 0; i + 1 < lines.length; i += 1) {
    const headerRow = canonicalizeMarkdownTableRow(lines[i]);
    if (!headerRow) {
      continue;
    }

    const headerCells = headerRow
      .slice(1, -1)
      .split("|")
      .map((cell) => `${cell ?? ""}`.trim());
    if (canonicalizeMarkdownTableSeparatorLine(lines[i + 1], headerCells.length)) {
      return true;
    }
  }

  return false;
}

function normalizeMarkdownTableSeparators(text) {
  if (!text) {
    return "";
  }

  const dashVariantsRegex = /[\u2014\u2013\u2011\u2212\u2500\u2012]/g;
  const lines = text.split("\n");
  let changed = false;

  const normalizedLines = lines.map((line) => {
    const trimmed = `${line ?? ""}`.trim();
    if (!trimmed.includes("|")) {
      return line;
    }

    let candidate = trimmed;
    if (!candidate.startsWith("|")) {
      candidate = `|${candidate}`;
    }
    if (!candidate.endsWith("|")) {
      candidate = `${candidate}|`;
    }

    if (!/^\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]+\s*(\|\s*[:\-\u2014\u2013\u2011\u2212\u2500\u2012]+\s*)+\|$/.test(candidate)) {
      return line;
    }

    const rawCells = candidate
      .slice(1, -1)
      .split("|")
      .map((cell) => `${cell ?? ""}`.trim());
    if (rawCells.length < 2) {
      return line;
    }

    const normalizedCells = [];
    for (const cell of rawCells) {
      const compact = `${cell ?? ""}`.replace(/\s+/g, "").replace(dashVariantsRegex, "-");
      if (!/^:?-+:?$/.test(compact)) {
        return line;
      }

      const leadingColon = compact.startsWith(":") ? ":" : "";
      const trailingColon = compact.endsWith(":") ? ":" : "";
      const dashCount = Math.max(3, countMatches(compact, /-/g));
      normalizedCells.push(`${leadingColon}${"-".repeat(dashCount)}${trailingColon}`);
    }

    const leadingMatch = `${line ?? ""}`.match(/^\s*/);
    const leadingWhitespace = leadingMatch ? leadingMatch[0] : "";
    const rebuilt = `${leadingWhitespace}| ${normalizedCells.join(" | ")} |`;
    if (rebuilt !== line) {
      changed = true;
    }

    return rebuilt;
  });

  return changed ? normalizedLines.join("\n") : text;
}

function isMarkdownTableRow(line) {
  return !!canonicalizeMarkdownTableRow(line);
}

function collapseMarkdownTableBlankLines(text) {
  if (!text) {
    return "";
  }

  const lines = text.split("\n");
  if (lines.length < 3) {
    return text;
  }

  const compact = [];
  const findNextNonEmpty = (startIndex) => {
    for (let i = Math.max(0, startIndex); i < lines.length; i += 1) {
      if (`${lines[i] ?? ""}`.trim().length > 0) {
        return lines[i];
      }
    }
    return "";
  };

  lines.forEach((line, index) => {
    if (`${line ?? ""}`.trim().length === 0) {
      const prev = compact.length > 0 ? compact[compact.length - 1] : "";
      const next = findNextNonEmpty(index + 1);
      if (isMarkdownTableRow(prev) && isMarkdownTableRow(next)) {
        return;
      }
    }
    compact.push(line);
  });

  return compact.join("\n");
}

function isMarkdownTableSeparatorLine(line) {
  return !!canonicalizeMarkdownTableSeparatorLine(line);
}

function renderFallbackInlineMarkdown(value) {
  let html = escapeHtml(`${value ?? ""}`);
  // 인라인 코드 (백틱)
  html = html.replace(/`([^`\n]+)`/g, "<code>$1</code>");
  // 볼드
  html = html.replace(/\*\*([^*\n][\s\S]*?)\*\*/g, "<strong>$1</strong>");
  html = html.replace(/__([^_\n][\s\S]*?)__/g, "<strong>$1</strong>");
  // 이탈릭
  html = html.replace(/\*([^*\n]+)\*/g, "<em>$1</em>");
  html = html.replace(/_([^_\n]+)_/g, "<em>$1</em>");
  // 취소선
  html = html.replace(/~~([^~\n]+)~~/g, "<del>$1</del>");
  return html;
}

function renderFallbackBlockLine(line) {
  const trimmed = `${line ?? ""}`.trim();
  // 헤더
  const headerMatch = trimmed.match(/^(#{1,6})\s+(.+)$/);
  if (headerMatch) {
    const level = headerMatch[1].length;
    return `<h${level}>${renderFallbackInlineMarkdown(headerMatch[2])}</h${level}>`;
  }
  // 비순서 리스트
  const ulMatch = trimmed.match(/^[-*+]\s+(.+)$/);
  if (ulMatch) {
    return `<li>${renderFallbackInlineMarkdown(ulMatch[1])}</li>`;
  }
  // 순서 리스트
  const olMatch = trimmed.match(/^\d+[.)]\s+(.+)$/);
  if (olMatch) {
    return `<li>${renderFallbackInlineMarkdown(olMatch[1])}</li>`;
  }
  // 인용
  const bqMatch = trimmed.match(/^>\s*(.*)$/);
  if (bqMatch) {
    return `<blockquote>${renderFallbackInlineMarkdown(bqMatch[1])}</blockquote>`;
  }
  return renderFallbackInlineMarkdown(line);
}

function splitMarkdownTableCells(line) {
  const normalizedRow = canonicalizeMarkdownTableRow(line);
  if (!normalizedRow) {
    return [];
  }

  return normalizedRow
    .slice(1, -1)
    .split("|")
    .map((cell) => renderFallbackInlineMarkdown(`${cell ?? ""}`.trim()));
}

function renderTableAwareFallbackHtml(text) {
  const lines = `${text ?? ""}`.split("\n");
  const chunks = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    if (isMarkdownTableRow(line) && i + 1 < lines.length && isMarkdownTableSeparatorLine(lines[i + 1])) {
      const headerCells = splitMarkdownTableCells(line);
      i += 2;
      const bodyRows = [];
      while (i < lines.length && isMarkdownTableRow(lines[i])) {
        bodyRows.push(splitMarkdownTableCells(lines[i]));
        i += 1;
      }

      if (headerCells.length >= 2) {
        let tableHtml = "<table><thead><tr>";
        headerCells.forEach((cell) => {
          tableHtml += `<th>${cell}</th>`;
        });
        tableHtml += "</tr></thead><tbody>";
        bodyRows.forEach((cells) => {
          tableHtml += "<tr>";
          for (let ci = 0; ci < headerCells.length; ci += 1) {
            tableHtml += `<td>${cells[ci] ?? ""}</td>`;
          }
          tableHtml += "</tr>";
        });
        tableHtml += "</tbody></table>";
        chunks.push(tableHtml);
        continue;
      }
    }

    if (`${line ?? ""}`.trim().length === 0) {
      chunks.push("<br>");
    } else {
      chunks.push(renderFallbackBlockLine(line));
    }
    i += 1;
  }

  return chunks.join("<br>").replace(/(?:<br>){3,}/g, "<br><br>");
}

function normalizeStructuredMarkdownArtifacts(value) {
  let text = `${value ?? ""}`;
  text = text.replace(/(\d+)\.\s*\n+(?=\d)/g, "$1.");
  text = text.replace(
    /(^|\n)(\d+\.)\s*\n+(?=\s*(?:\*\*[^*\n]+:\*\*|[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}:\s))/g,
    "$1$2 "
  );
  text = text.replace(
    /(^|\n)((?:\*\*[^*\n]+:\*\*)|(?:[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}:))\s+\*\*\s+/g,
    "$1$2 "
  );
  text = text.replace(
    /(^|\n)((?:\*\*[^*\n]+:\*\*)|(?:[A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-·\s]{0,80}:))\s+\*\*(?=\n|$)/g,
    "$1$2"
  );
  text = text.replace(
    /(^|\n)(?<lead>[-•▪]\s*)?(?<body>\d+[.)]\s*[^\n:*|]+)(?=\n|$)/g,
    (match, prefix, lead, body) => {
      const normalizedLead = `${lead ?? ""}`;
      const normalizedBody = `${body ?? ""}`.trim();
      if (!normalizedBody || /\*\*/.test(normalizedBody)) {
        return `${prefix}${normalizedLead}${normalizedBody}`;
      }

      const headline = normalizedBody.replace(/^\d+[.)]\s*/, "").trim();
      if (!headline
        || headline.length < 2
        || headline.length > 140
        || /[:：|]/.test(headline)
        || /https?:\/\//i.test(headline)
        || /^(출처|요약|핵심)/i.test(headline)
        || /(니다\.|습니다\.|다\.|요\.|[?!.])$/.test(headline)) {
        return `${prefix}${normalizedLead}${normalizedBody}`;
      }

      return `${prefix}${normalizedLead}**${normalizedBody}**`;
    }
  );

  // "레이블: 값" 패턴을 "**레이블:** 값"으로 변환 (볼드 강조)
  // 이미 **로 감싸진 줄, URL, 코드블록, 테이블 줄은 제외
  text = text.replace(
    /(^|\n)((?:[-•▪][ \t]*)?(?:(?:No\.\d+|\d+[.)][ \t]*)?)?)([A-Za-z가-힣0-9('‘’][A-Za-z가-힣0-9()'‘’,.&+_/\-· \t]{1,60}?)[ \t]*[:：][ \t]*(.*)$/gm,
    (match, prefix, lead, label, value) => {
      const trimLabel = label.trim();
      const trimValue = (value || "").trim();
      // 이미 볼드면 건너뜀
      if (/\*\*/.test(match)) return match;
      // URL 줄이면 건너뜀
      if (/https?:\/\//i.test(trimLabel)) return match;
      // 코드블록이나 테이블이면 건너뜀
      if (match.trim().startsWith("```") || match.trim().startsWith("|")) return match;
      // 너무 짧은 레이블 건너뜀
      if (trimLabel.length < 2) return match;
      // 출처/요약/핵심 같은 메타 레이블은 건너뜀
      if (/^(출처|sources?)\s*$/i.test(trimLabel)) return match;

      const formattedValue = trimValue ? ` ${trimValue}` : "";
      return `${prefix}${lead}**${trimLabel}:**${formattedValue}`;
    }
  );

  return text;
}

export function normalizeMarkdownSource(value) {
  let text = `${value ?? ""}`.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  const rawLineBreakCount = countMatches(text, /\n/g);

  if (rawLineBreakCount <= 1 && /\\n/.test(text)) {
    text = text
      .replace(/\\r\\n/g, "\n")
      .replace(/\\n/g, "\n")
      .replace(/\\t/g, "  ");
  }

  text = normalizeStructuredMarkdownArtifacts(text);
  text = normalizeMarkdownTableSeparators(text);
  text = normalizeMarkdownTableBlocks(text);
  text = collapseMarkdownTableBlankLines(text);

  const markdownSignalCount =
    countMatches(text, /(^|\s)#{1,6}\s/gm)
    + countMatches(text, /(^|\s)>\s/gm)
    + countMatches(text, /(^|\s)(?:[-*+])\s/gm)
    + countMatches(text, /(^|\s)\d+\.\s/gm)
    + countMatches(text, /```/g)
    + countMatches(text, /\|\s*[-:]{3,}\s*\|/g)
    + countMatches(text, /\[[^\]]+\]\([^)]+\)/g);

  if (countMatches(text, /\n/g) <= 2 && markdownSignalCount >= 2) {
    text = text
      .replace(/\s+(?=#{1,6}\s)/g, "\n")
      .replace(/\s+(?=>\s)/g, "\n")
      .replace(/\s+(?=\d+\.\s)/g, "\n")
      .replace(/\s+(?=[*+-]\s)/g, "\n");

    if (/\|\s*[-:]{3,}\s*\|/.test(text)) {
      text = text
        .replace(/\|\s+\|/g, "|\n|")
        .replace(/\n{3,}/g, "\n\n");
    }
  }

  text = text
    .replace(/[ \t]+\n/g, "\n")
    .replace(/([^\n])\n(?=(#{1,6}\s|[-*+]\s|\d+\.\s|>\s))/g, "$1\n\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();

  // 일반 텍스트 줄 사이의 단일 \n → \n\n 변환 (문단 분리).
  // 코드블록(```) 내부, 테이블(|), 리스트(-/*/+/숫자.), 헤더(#) 줄은 제외.
  const codeBlockRegex = /```[\s\S]*?```/g;
  const codeBlocks = [];
  text = text.replace(codeBlockRegex, (match) => {
    const placeholder = `\x00CODE${codeBlocks.length}\x00`;
    codeBlocks.push(match);
    return placeholder;
  });

  const lines = text.split("\n");
  const result = [];
  for (let li = 0; li < lines.length; li++) {
    result.push(lines[li]);
    if (li < lines.length - 1) {
      const curr = lines[li].trim();
      const next = lines[li + 1].trim();
      // 현재 줄이나 다음 줄이 빈 줄이면 이미 문단 분리
      if (!curr || !next) {
        result.push("");
        continue;
      }
      // 코드블록 placeholder면 건드리지 않음
      if (curr.startsWith("\x00CODE") || next.startsWith("\x00CODE")) {
        result.push("");
        continue;
      }
      // 다음 줄이 마크다운 특수 구문이면 이미 위에서 \n\n 처리됨
      const isSpecialNext = /^(#{1,6}\s|[-*+]\s|\d+[.)]\s|>\s|\|)/.test(next);
      // 다음 줄이 새로운 마크다운 특수 구문이 아닐 때, 현재 줄이 문장 끝이면 문단 분리
      if (!isSpecialNext) {
        // 현재 줄이 문장 종결 어미, 문장 부호, 또는 항목의 끝으로 보이는지 확인
        const isSentenceEnd = /[.!?…~:;%)\]"']|다\.|요\.|습니다\.|입니다\.|]$/.test(curr) || curr.endsWith("**");
        if (isSentenceEnd) {
          result.push("");
        }
      }
    }
  }
  text = result.join("\n").replace(/\n{3,}/g, "\n\n").trim();

  // 코드블록 복원
  codeBlocks.forEach((block, idx) => {
    text = text.replace(`\x00CODE${idx}\x00`, block);
  });

  return text;
}

function createMarkdownRenderer(windowLike) {
  try {
    if (!windowLike || typeof windowLike.markdownit !== "function") {
      return null;
    }

    const renderer = windowLike.markdownit({
      html: true,
      linkify: true,
      breaks: true,
      typographer: false
    });
    if (renderer.linkify && typeof renderer.linkify.set === "function") {
      renderer.linkify.set({
        fuzzyLink: false,
        fuzzyEmail: false,
        fuzzyIP: false
      });
    }

    if (typeof windowLike.markdownitFootnote === "function") {
      renderer.use(windowLike.markdownitFootnote);
    }

    const originalLinkOpen = renderer.renderer.rules.link_open
      || ((tokens, idx, options, env, self) => self.renderToken(tokens, idx, options));
    renderer.renderer.rules.link_open = (tokens, idx, options, env, self) => {
      tokens[idx].attrSet("target", "_blank");
      tokens[idx].attrSet("rel", "noopener noreferrer");
      return originalLinkOpen(tokens, idx, options, env, self);
    };

    return renderer;
  } catch (_err) {
    return null;
  }
}

export function createMarkdownSupport({ React, window: windowLike }) {
  const { useEffect, useMemo, useRef } = React;
  let markdownRenderer = createMarkdownRenderer(windowLike);

  function getMarkdownRenderer() {
    if (!markdownRenderer) {
      markdownRenderer = createMarkdownRenderer(windowLike);
    }
    return markdownRenderer;
  }

  function renderMarkdownToSafeHtml(value) {
    const text = normalizeMarkdownSource(value);
    let html = "";
    const renderer = getMarkdownRenderer();

    if (renderer) {
      html = renderer.render(text);
      if (hasMarkdownTableBlock(text) && !/<table[\s>]/i.test(html)) {
        html = renderTableAwareFallbackHtml(text);
      }
    } else {
      html = renderTableAwareFallbackHtml(text);
    }

    if (windowLike && windowLike.DOMPurify && typeof windowLike.DOMPurify.sanitize === "function") {
      html = windowLike.DOMPurify.sanitize(html, {
        USE_PROFILES: { html: true },
        ADD_TAGS: ["table", "thead", "tbody", "tr", "th", "td", "img", "hr", "sup", "sub"],
        ADD_ATTR: ["target", "rel", "class", "id"]
      });
    }

    return html;
  }

  function MarkdownBubbleText(props) {
    const hostRef = useRef(null);
    const html = useMemo(() => renderMarkdownToSafeHtml(props && props.text ? props.text : ""), [props && props.text]);

    useEffect(() => {
      if (!hostRef.current) {
        return;
      }

      if (windowLike && typeof windowLike.renderMathInElement === "function") {
        try {
          windowLike.renderMathInElement(hostRef.current, {
            delimiters: [
              { left: "$$", right: "$$", display: true },
              { left: "$", right: "$", display: false },
              { left: "\\(", right: "\\)", display: false },
              { left: "\\[", right: "\\]", display: true }
            ],
            ignoredTags: ["script", "noscript", "style", "textarea", "pre", "code"],
            throwOnError: false,
            strict: "ignore"
          });
        } catch (_err) {
        }
      }
    }, [html]);

    return React.createElement("div", {
      className: "bubble-text markdown",
      ref: hostRef,
      dangerouslySetInnerHTML: { __html: html }
    });
  }

  return {
    MarkdownBubbleText,
    renderMarkdownToSafeHtml
  };
}
