import * as vscode from 'vscode'
import * as languageClient from './language-client'
import * as debuggerClient from './debugger-client'
import * as profiling from './profiling'
import * as utils from './utils'
import path from 'path'
import * as net from 'net'
import * as rpc from 'vscode-jsonrpc/node'

export let log: vscode.LogOutputChannel
export let token: string | null = null

export type GameUnit = {
    id: string,
    signal: string,
    source: string | null,
}

function authenticate() {
    return new Promise<string>((resolve, reject) => {
        const socket = net.connect({
            host: '127.0.0.1',
            port: 8051,
        })
        socket.on('connect', () => {
            log.info(`[ExtClient] [TCP] Connected`)
            let connection = rpc.createMessageConnection(
                new rpc.StreamMessageReader(socket),
                new rpc.StreamMessageWriter(socket))

            connection.listen()

            log.info(`[ExtClient] Authenticating ...`)
            connection.sendRequest('authenticate')
                .then(_token => {
                    log.info(`[ExtClient] Received token: "${_token}"`)
                    resolve(String(_token))
                })
                .catch(reject)
                .finally(() => {
                    log.info(`[ExtClient] Closing connection`)
                    connection.end()
                    connection.dispose()
                    log.info(`[ExtClient] [TCP] Ending socket`)
                    socket.end(() => {
                        socket.destroy()
                        log.info(`[ExtClient] [TCP] Socket destroyed`)
                    })
                })
        })
        socket.on('error', (error) => {
            log.error(`[ExtClient] [TCP] ${error}`)
            reject(error)
        })
        socket.on('connectionAttempt', (ip, port, family) => log.trace(`[ExtClient] [TCP] Connection attempt to ${ip}:${port} ${family}`))
        socket.on('connectionAttemptFailed', (ip, port, family, error) => log.trace(`[ExtClient] [TCP] Connection attempt to ${ip}:${port} ${family} failed ${error}`))
        socket.on('connectionAttemptTimeout', (ip, port, family) => log.trace(`[ExtClient] [TCP] Connection attempt to ${ip}:${port} ${family} timed out`))
        socket.on('close', () => log.info(`[ExtClient] [TCP] Closed`))
    })
}

export function activate(context: vscode.ExtensionContext) {
    log = vscode.window.createOutputChannel("Nothingame Extension", { log: true })

    const authenticateStatusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right)
    authenticateStatusItem.name = `Nothingame Status`
    authenticateStatusItem.hide()
    context.subscriptions.push(authenticateStatusItem)

    const onAuthenticate = () => {
        log.info(`Authenticated with token ${token}`)
        unitsTreeDataProvider.refresh()
        authenticateStatusItem.show()
        authenticateStatusItem.text = `$(pass-filled) Authenticated`
    }

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.units.refresh`, () => {
        unitsTreeDataProvider.refresh()
    }))

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.authenticate`, (_token: string) => {
        token = _token
    }))

    context.subscriptions.push(vscode.window.registerUriHandler({
        handleUri(uri) {
            const url = URL.parse(uri.toString(true))
            if (!url) {
                vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}"`)
                return
            }

            switch (url.pathname) {
                case '/debug': {
                    const entity = url.searchParams.get('entity')
                    const ghost = url.searchParams.get('ghost')
                    const _token = url.searchParams.get('token')

                    if (!entity && !ghost) {
                        vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}": Parameter "entity" or "ghost" is required`)
                        break
                    }

                    if (!_token) {
                        vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}": Parameter "token" is required`)
                        break
                    }

                    if (token !== _token) {
                        token = _token
                        onAuthenticate()
                    }

                    log.debug('[Debugger] Try to start debugging ...')

                    debuggerClient.startDebugging(_token, entity, ghost)
                    break
                }
                case '/authenticate': {
                    const _token = url.searchParams.get('token')

                    if (!_token) {
                        vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}": Parameter "token" is required`)
                        break
                    }

                    token = _token
                    onAuthenticate()
                    break
                }
                default:
                    vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}": Unknown pathname "${url.pathname}"`)
                    break
            }
        },
    }))

    const unitsTreeDataProvider = new class implements vscode.TreeDataProvider<GameUnit> {
        constructor() { }

        private _onDidChangeTreeData = new vscode.EventEmitter<GameUnit | undefined>();
        readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

        refresh(): void {
            this._onDidChangeTreeData.fire(undefined)
        }

        getTreeItem(element: GameUnit): vscode.TreeItem {
            const res = new vscode.TreeItem(`${element.id} ${element.source ? path.basename(element.source) : ''}`.trim(), vscode.TreeItemCollapsibleState.None)
            res.tooltip = element.source ?? 'No source'
            res.contextValue = `${utils.extensionId}Entity`
            res.iconPath = ({
                //'off': { id: '', color: { id: 'disabledForeground' } },
                'debugged': new vscode.ThemeIcon('debug', new vscode.ThemeColor('debugIcon.disconnectForeground')),
                'running': new vscode.ThemeIcon('check'),
                'halted': new vscode.ThemeIcon('debug-pause', new vscode.ThemeColor('gauge.warningForeground')),
                'crashed': new vscode.ThemeIcon('warning', new vscode.ThemeColor('gauge.errorForeground')),
            } as Record<string, vscode.ThemeIcon>)[element.signal]
            if (element.source && (element.source.startsWith('/') || element.source.startsWith('\\'))) {
                res.resourceUri = vscode.Uri.file(element.source)
                res.command = {
                    command: 'vscode.open',
                    title: 'Open File',
                    arguments: [element.source],
                }
            }
            return res
        }

        getChildren(element?: GameUnit): Thenable<GameUnit[]> {
            if (!element) {
                if (!languageClient.client) return Promise.resolve([])
                if (!token) return Promise.resolve([])
                return languageClient.client.client.sendRequest('units', { token: token })
            }
            return Promise.resolve([])
        }
    }()
    context.subscriptions.push(vscode.window.createTreeView('gameUnits', { treeDataProvider: unitsTreeDataProvider }))

    languageClient.activate(context)
    debuggerClient.activate(context)
    profiling.activate(context)

    const _auth = () => {
        authenticate().then(v => token = v).catch(error => {
            vscode.window.showErrorMessage(`Failed to authenticate the extension: ${error}`, 'Retry')
                .then(res => {
                    if (res === 'Retry') {
                        _auth()
                    }
                })
        })
    }
    //_auth()
}

export function deactivate() {
    languageClient.deactivate()
    debuggerClient.deactivate()
}
