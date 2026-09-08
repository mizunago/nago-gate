import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import type { PublishConfig } from "./config.js";

const GITHUB_API = "https://api.github.com";

function githubHeaders(token: string): Record<string, string> {
  return {
    Authorization: `Bearer ${token}`,
    Accept: "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
    "User-Agent": "supporter-gate-bot",
  };
}

async function gh(url: string, headers: Record<string, string>, body: unknown): Promise<Record<string, string>> {
  const res = await fetch(url, { method: "POST", headers: { ...headers, "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!res.ok) throw new Error(`GitHub API 失敗 ${url}: ${res.status} ${await res.text()}`);
  return (await res.json()) as Record<string, string>;
}

/** 親コミットなしのブランチを作り、path に content を置く */
async function createOrphanBranch(repo: string, headers: Record<string, string>, branch: string, path: string, content: string): Promise<void> {
  const blob = await gh(`${repo}/git/blobs`, headers, { content, encoding: "utf-8" });
  const tree = await gh(`${repo}/git/trees`, headers, { tree: [{ path, mode: "100644", type: "blob", sha: blob.sha }] });
  const commit = await gh(`${repo}/git/commits`, headers, { message: "Create supporters list branch", tree: tree.sha, parents: [] });
  await gh(`${repo}/git/refs`, headers, { ref: `refs/heads/${branch}`, sha: commit.sha });
}

/** 公開先へ JSON 文字列を書き込む。戻り値は Udon 側に設定する URL のヒント */
export async function publishJson(
  config: PublishConfig,
  content: string,
  githubToken: string | null,
): Promise<string> {
  switch (config.type) {
    case "file": {
      const target = path.resolve(config.path);
      mkdirSync(path.dirname(target), { recursive: true });
      writeFileSync(target, content, "utf8");
      return target;
    }
    case "gist": {
      if (!githubToken) throw new Error("publish.type=gist には GITHUB_TOKEN が必要です");
      const res = await fetch(`${GITHUB_API}/gists/${config.gistId}`, {
        method: "PATCH",
        headers: { ...githubHeaders(githubToken), "Content-Type": "application/json" },
        body: JSON.stringify({ files: { [config.fileName]: { content } } }),
      });
      if (!res.ok) throw new Error(`Gist 更新に失敗: ${res.status} ${await res.text()}`);
      const body = (await res.json()) as { owner?: { login?: string }; id: string };
      const login = body.owner?.login ?? "<user>";
      return `https://gist.githubusercontent.com/${login}/${body.id}/raw/${config.fileName}`;
    }
    case "github": {
      if (!githubToken) throw new Error("publish.type=github には GITHUB_TOKEN が必要です");
      const branch = config.branch ?? "main";
      const repo = `${GITHUB_API}/repos/${config.owner}/${config.repo}`;
      const headers = githubHeaders(githubToken);
      const pagesUrl = `https://${config.owner}.github.io/${config.repo}/${config.path}`;

      // ブランチが無ければ、このファイルだけを含む孤立ブランチを作る（gh-pages 用）
      const br = await fetch(`${repo}/branches/${encodeURIComponent(branch)}`, { headers });
      if (br.status === 404) {
        await createOrphanBranch(repo, headers, branch, config.path, content);
        return pagesUrl;
      }
      if (!br.ok) throw new Error(`GitHub branch 取得に失敗: ${br.status} ${await br.text()}`);

      const base = `${repo}/contents/${config.path}`;
      let sha: string | undefined;
      const head = await fetch(`${base}?ref=${encodeURIComponent(branch)}`, { headers });
      if (head.ok) {
        sha = ((await head.json()) as { sha: string }).sha;
      } else if (head.status !== 404) {
        throw new Error(`GitHub contents 取得に失敗: ${head.status} ${await head.text()}`);
      }
      const res = await fetch(base, {
        method: "PUT",
        headers: { ...headers, "Content-Type": "application/json" },
        body: JSON.stringify({
          message: "Update supporters list",
          content: Buffer.from(content, "utf8").toString("base64"),
          branch,
          ...(sha ? { sha } : {}),
        }),
      });
      if (!res.ok) throw new Error(`GitHub contents 更新に失敗: ${res.status} ${await res.text()}`);
      return pagesUrl;
    }
    default: {
      const never: never = config;
      throw new Error(`未知の publish.type: ${JSON.stringify(never)}`);
    }
  }
}
