import * as vscode from 'vscode'
import {
    NotificationType,
    ParameterStructures,
    RequestType
} from 'vscode-jsonrpc/node'

export interface SessionEnvelope { sessionId: string; message: vscode.DebugProtocolMessage }
export interface SessionRef { sessionId: string }
export interface SessionStart { sessionId: string; name?: string; configuration?: Record<string, unknown> }
export interface AuthenticateRequestParams { token: string }
export interface AuthenticateResponseArguments { success: boolean }

export const MessageNotification = new NotificationType<SessionEnvelope>('dap/message', ParameterStructures.byName)
export const CloseNotification = new NotificationType<SessionRef>('dap/close', ParameterStructures.byName)
export const ClosedNotification = new NotificationType<SessionRef>('dap/closed', ParameterStructures.byName)
export const StartNotification = new NotificationType<SessionStart>('dap/start', ParameterStructures.byName)
export const AuthenticateRequest = new RequestType<void, boolean, void>('authenticate', ParameterStructures.byName)
