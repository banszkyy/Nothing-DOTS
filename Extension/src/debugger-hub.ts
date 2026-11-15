import * as vscode from 'vscode'
import {
    MessageConnection,
    NotificationType,
    ParameterStructures,
    RequestParam,
} from 'vscode-jsonrpc/node'
import { log } from './extension'

interface SessionEnvelope { sessionId: string; message: vscode.DebugProtocolMessage }
interface SessionRef { sessionId: string }
interface SessionStart { sessionId: string; name?: string; configuration?: Record<string, unknown> }

const MessageNotification = new NotificationType<SessionEnvelope>('dap/message', ParameterStructures.byName)
const CloseNotification = new NotificationType<SessionRef>('dap/close', ParameterStructures.byName)
const ClosedNotification = new NotificationType<SessionRef>('dap/closed', ParameterStructures.byName)
const StartNotification = new NotificationType<SessionStart>('dap/start', ParameterStructures.byName)

interface DapMessage { seq: number; type: string; command?: string; request_seq?: number }

export class DebugSessionHub implements vscode.Disposable {
    private readonly connection: MessageConnection
    readonly sessions = new Map<string, MuxedDebugAdapter>();
    private readonly lostEmitter = new vscode.EventEmitter<void>();
    private lost = false;

    readonly onDidLoseConnection = this.lostEmitter.event;

    constructor(connection: MessageConnection, debugType: string) {
        this.connection = connection

        this.connection.onClose(() => this.connectionLost())

        this.connection.onNotification(StartNotification, async ({ sessionId, name, configuration }) => {
            log.debug(`[DebugSessionHub] Starting debugging because a notification told to`)
            const started = await vscode.debug.startDebugging(undefined, {
                ...configuration,
                type: debugType,
                request: 'attach',
                name: name ?? `Session ${sessionId}`,
                sessionId,
            })
            if (!started && !this.sessions.has(sessionId)) {
                this.notify(CloseNotification, { sessionId })
            }
        })

        this.connection.onNotification(MessageNotification, ({ sessionId, message }) => {
            this.sessions.get(sessionId)?.deliver(message)
        })

        this.connection.onNotification(ClosedNotification, ({ sessionId }) => {
            log.debug(`[DebugSessionHub] Stopping debugging because a notification told to`)
            const s = this.sessions.get(sessionId)
            if (!s) { return }
            this.sessions.delete(sessionId)
            s.terminateLocally('The debug host closed the session.')
        })

        this.connection.listen()
    }

    createAdapter(session: vscode.DebugSession): MuxedDebugAdapter {
        if (this.lost) {
            throw new Error('The connection to the debug host has been lost.')
        }
        const sessionId = session.configuration['sessionId'] as string
        if (this.sessions.has(sessionId)) {
            throw new Error(`Debug session '${sessionId}' already exists.`)
        }
        const adapter = new MuxedDebugAdapter(
            sessionId,
            session,
            message => this.notify(MessageNotification, { sessionId, message }),
            () => {
                if (this.sessions.delete(sessionId)) {
                    this.notify(CloseNotification, { sessionId })
                }
            })
        this.sessions.set(sessionId, adapter)
        return adapter
    }

    createInlineImplementation(session: vscode.DebugSession): vscode.DebugAdapterInlineImplementation {
        return new vscode.DebugAdapterInlineImplementation(this.createAdapter(session))
    }

    dispose(): void {
        this.lost = true
        for (const s of [...this.sessions.values()]) { s.terminateLocally('The debug hub was disposed.') }
        this.sessions.clear()
        this.connection.dispose()
        this.lostEmitter.dispose()
    }

    private connectionLost(): void {
        if (this.lost) { return }
        this.lost = true
        const owned = [...this.sessions.values()]
        this.sessions.clear()
        for (const s of owned) { s.terminateLocally('The connection to the debug host was lost.') }
        this.lostEmitter.fire()
    }

    private notify<P>(type: NotificationType<P>, params: NoInfer<RequestParam<P>>): void {
        if (this.lost) { return }
        this.connection.sendNotification(type, params).catch(() => this.connectionLost())
    }
}

export class MuxedDebugAdapter implements vscode.DebugAdapter {
    private readonly emitter = new vscode.EventEmitter<vscode.DebugProtocolMessage>();
    private readonly closeEmitter = new vscode.EventEmitter<void>();
    private readonly pending = new Map<number, string>();
    private nextSeq = 1;
    private dead = false;
    private disposed = false;

    readonly onDidSendMessage = this.emitter.event;
    readonly onDidClose = this.closeEmitter.event;

    constructor(
        readonly sessionId: string,
        readonly session: vscode.DebugSession,
        private readonly send: (message: vscode.DebugProtocolMessage) => void,
        private readonly onLocalDispose: () => void) { }

    handleMessage(message: vscode.DebugProtocolMessage): void {
        if (this.disposed) { return }
        const m = message as unknown as DapMessage

        if (this.dead) {
            if (m.type === 'request') {
                const teardown = m.command === 'disconnect' || m.command === 'terminate'
                this.reply(m.seq, m.command!, teardown, 'Debug host connection lost.')
            }
            return
        }

        if (m.type === 'request') { this.pending.set(m.seq, m.command!) }
        this.send(message)
    }

    deliver(message: vscode.DebugProtocolMessage): void {
        if (this.disposed || this.dead) { return }
        const m = message as unknown as DapMessage
        if (m.type === 'response' && m.request_seq !== undefined) { this.pending.delete(m.request_seq) }
        this.emitter.fire(message)
    }

    terminateLocally(reason: string): void {
        if (this.disposed || this.dead) { return }
        this.dead = true
        log.debug(`[MuxedDebugAdapter] Terminating debug session`)

        for (const [seq, command] of this.pending) { this.reply(seq, command, false, reason) }
        this.pending.clear()

        this.emitter.fire({ seq: this.nextSeq++, type: 'event', event: 'terminated' } as vscode.DebugProtocolMessage)
        this.closeEmitter.fire()
        void vscode.debug.stopDebugging(this.session)
    }

    dispose(): void {
        if (this.disposed) { return }
        this.disposed = true
        this.onLocalDispose()
        this.emitter.dispose()
        this.closeEmitter.dispose()
    }

    private reply(requestSeq: number, command: string, success: boolean, message: string): void {
        this.emitter.fire({
            seq: this.nextSeq++,
            type: 'response',
            request_seq: requestSeq,
            command,
            success,
            ...(success ? {} : { message }),
        } as vscode.DebugProtocolMessage)
    }
}
