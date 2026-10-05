// 管理者かどうかの判定。スラッシュコマンドと、ボタン（メンバーの申請の認定）の両方から使う。
import { PermissionFlagsBits, type GuildMember } from "discord.js";
import type { AppConfig } from "./config.js";

export function isAdmin(config: AppConfig, member: GuildMember): boolean {
  if (member.permissions.has(PermissionFlagsBits.ManageGuild)) return true;
  return config.adminRoleIds.some((id) => member.roles.cache.has(id));
}
