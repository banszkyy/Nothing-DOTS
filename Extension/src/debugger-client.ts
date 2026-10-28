'use strict'

import * as vscode from 'vscode'
import { WorkspaceFolder, DebugConfiguration, ProviderResult, CancellationToken } from 'vscode'
import * as fs from 'fs'
import { extensionId } from './utils'
import { GameUnit, log } from './extension'

/*
type UnitModule = {
    type: string
    address: number
    fields: Record<string, UnitModuleType>
}

type UnitModuleType = {
    type: string
    offset: number
}

type UnitModuleTreeViewItem = UnitModule | UnitModuleType

let unitModulesTreeView: vscode.TreeView<UnitModuleTreeViewItem> | null = null
*/

const MaxUnitLogLength = 50

let onStartDebugging = () => { }

export function startDebugging(token: string, entity: string | null, ghost: string | null) {
    log.debug(`[Debugger] Start debuging entity ${entity} ghost ${ghost} ...`)
    vscode.debug.startDebugging(undefined, {
        type: extensionId,
        name: 'Debug Entity',
        request: 'attach',
        entity: entity,
        ghost: ghost,
        token: token,
    })
        .then(result => {
            if (!result) {
                vscode.window.showErrorMessage('Failed to start debugging')
                log.warn('[Debugger] Failed to start debugging')
            } else {
                log.info('[Debugger] Debugging started')

                onStartDebugging()

                /*
                const unitModulesTreeDataProvider = new class implements vscode.TreeDataProvider<UnitModuleTreeViewItem> {
                    constructor() { }

                    private _onDidChangeTreeData = new vscode.EventEmitter<UnitModuleTreeViewItem | undefined>();
                    readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

                    refresh(): void {
                        this._onDidChangeTreeData.fire(undefined)
                    }

                    getTreeItem(element: UnitModuleTreeViewItem): vscode.TreeItem {
                        if ('address' in element) {
                            return new vscode.TreeItem(`${element.type} ${element.address}`, vscode.TreeItemCollapsibleState.None)
                        } else {
                            return new vscode.TreeItem(`${element.type} ${element.offset}`, vscode.TreeItemCollapsibleState.None)
                        }
                    }

                    getChildren(element?: GameUnit): Thenable<UnitModuleTreeViewItem[]> {
                        if (!element) {
                            if (!languageClient.client) return Promise.resolve([])
                            if (!token) return Promise.resolve([])
                            return languageClient.client.client.sendRequest('modules', { token: token, entity:  })
                                .then(() => {
                                    return []
                                })
                        }
                        return Promise.resolve([])
                    }
                }()
                if (unitModulesTreeView) {
                    unitModulesTreeView.dispose()
                }

                unitModulesTreeView = vscode.window.createTreeView('unitModules', { treeDataProvider: unitModulesTreeDataProvider })
                */
            }
        }, error => {
            log.error(`[Debugger] Failed to start debugging`, error)
        })
}

export function activate(context: vscode.ExtensionContext) {
    log.debug('[Debugger] Activating debugger ...')

    onStartDebugging = () => {
        unitLogs.splice(0, unitLogs.length)
    }

    let adapterFactory: vscode.DebugAdapterDescriptorFactory
    let trackerFactory: vscode.DebugAdapterTrackerFactory

    adapterFactory = new class implements vscode.DebugAdapterDescriptorFactory {
        createDebugAdapterDescriptor(session: vscode.DebugSession, executable: vscode.DebugAdapterExecutable | undefined): ProviderResult<vscode.DebugAdapterDescriptor> {
            log.trace(`[Debugger] Registering adapter descriptor with ${'127.0.0.1'}:${8053}`)
            return new vscode.DebugAdapterServer(8053, '127.0.0.1')
        }
    }()

    trackerFactory = new class implements vscode.DebugAdapterTrackerFactory {
        createDebugAdapterTracker(session: vscode.DebugSession): ProviderResult<vscode.DebugAdapterTracker> {
            log.trace(`[Debugger] Registering adapter tracker`, session)
            return {
                //onDidSendMessage(message: any): void {
                //    log.trace(`[Debugger] <<`, message)
                //},
                //onWillReceiveMessage(message) {
                //    log.trace(`[Debugger] >>`, message)
                //},
                onWillStartSession() {
                    log.info(`[Debugger] Will start session`)
                },
                onWillStopSession() {
                    log.info(`[Debugger] Will stop session`)
                },
                onError(error: Error): void {
                    log.error(`[Debugger] Error`, error)
                },
                onExit(code, signal) {
                    log.debug(`[Debugger] Exit code: ${code} signal: ${signal}`)
                },
            }
        }
    }

    log.debug('[Debugger] Registering adapter descriptor factory')
    context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory(extensionId, adapterFactory))
    log.debug('[Debugger] Registering adapter tracker factory')
    context.subscriptions.push(vscode.debug.registerDebugAdapterTrackerFactory(extensionId, trackerFactory))

    if ('dispose' in adapterFactory &&
        typeof adapterFactory.dispose === 'function' &&
        adapterFactory.dispose instanceof Function) {
        context.subscriptions.push(adapterFactory as { dispose(): void })
    }

    if ('dispose' in trackerFactory &&
        typeof trackerFactory.dispose === 'function' &&
        trackerFactory.dispose instanceof Function) {
        context.subscriptions.push(trackerFactory as { dispose(): void })
    }

    context.subscriptions.push(vscode.commands.registerCommand(`${extensionId}.debug.attach`, (entity: string | GameUnit, token: string) => {
        log.debug('[Debugger] Try to start debugging ...')

        if (!entity) {
            log.warn('[Debugger] No entity provided')
            vscode.window.showErrorMessage(`No entity provided`)
            return
        }

        if (typeof entity === 'object') {
            entity = entity.id
        }

        startDebugging(token, entity, null)
    }))

    vscode.debug.onDidStartDebugSession(e => log.trace('[Debugger] Debug session started:', e))
    vscode.debug.onDidChangeActiveDebugSession(e => log.trace('[Debugger] Active debug session changed:', e))
    vscode.debug.onDidTerminateDebugSession(e => log.trace('[Debugger] Debug session terminated:', e))
    vscode.debug.onDidChangeBreakpoints(e => log.trace('[Debugger] Breakpoints changed:', e))

    const outputChannel = vscode.window.createOutputChannel("Nothingame Debug Host", { log: true })

    type UnitLogItem = {
        logType: string,
        index: number,
        timestamp?: number,
        content?: any,
    }

    type UnitLogTreeViewItem = UnitLogItem | { key: string | number, value: any }

    const unitLogs: Array<UnitLogItem> = []

    const unitLogTreeDataProvider = new class implements vscode.TreeDataProvider<UnitLogTreeViewItem> {
        constructor() { }

        private _onDidChangeTreeData = new vscode.EventEmitter<UnitLogTreeViewItem | undefined>();
        readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

        refresh(): void {
            this._onDidChangeTreeData.fire(undefined)
        }

        getTreeItem(element: UnitLogTreeViewItem): vscode.TreeItem {
            if ('logType' in element) {
                const res = new vscode.TreeItem(`${element.logType}`, vscode.TreeItemCollapsibleState.None)
                res.id = `${element.index}`

                res.iconPath = new vscode.ThemeIcon('debug-breakpoint-log')

                if (element.content && Object.keys(element.content).length) {
                    res.collapsibleState = vscode.TreeItemCollapsibleState.Collapsed
                }

                if (element.timestamp) {
                    const date = new Date(element.timestamp * 1000)
                    res.description = `${date.getHours()}:${date.getMinutes().toString().padStart(2, '0')}:${date.getSeconds().toString().padStart(2, '0')}`
                }

                return res
            } else {
                if (element.value === null) {
                    return new vscode.TreeItem(`${element.key} = null`, vscode.TreeItemCollapsibleState.None)
                } else if (typeof element.value === 'object') {
                    return new vscode.TreeItem(`${element.key} = ...`, vscode.TreeItemCollapsibleState.Collapsed)
                } else {
                    return new vscode.TreeItem(`${element.key} = ${element.value}`, vscode.TreeItemCollapsibleState.None)
                }
            }
        }

        getChildren(element?: UnitLogTreeViewItem): Thenable<UnitLogTreeViewItem[]> {
            if (!element) {
                return Promise.resolve(unitLogs)
            } else {
                let content = null
                if ('logType' in element) {
                    content = element.content
                } else {
                    content = element.value
                }

                if (!content) return Promise.resolve([])

                const res = []
                if (Array.isArray(content)) {
                    for (let i = 0; i < content.length; i++) {
                        res.push({ key: i, value: content[i] })
                    }
                } else {
                    for (const key in content) {
                        const value = content[key]
                        res.push({ key, value })
                    }
                }
                return Promise.resolve(res)
            }
        }
    }()

    let treeViewRefreshTimeout: NodeJS.Timeout | null = null

    context.subscriptions.push(vscode.window.createTreeView('unitLog', { treeDataProvider: unitLogTreeDataProvider }))

    vscode.debug.onDidReceiveDebugSessionCustomEvent(e => {
        if (e.event === "adapterLog") {
            switch (e.body.level) {
                case 'trace': outputChannel.trace(e.body.message); break
                case 'debug': outputChannel.debug(e.body.message); break
                case 'info': outputChannel.info(e.body.message); break
                case 'warn': outputChannel.warn(e.body.message); break
                case 'error': outputChannel.error(e.body.message); break
                default: outputChannel.appendLine(e.body.message); break
            }
        } else if (e.event === "unitLog") {
            const v = e.body as UnitLogItem
            const last = unitLogs[0]
            if (last && last.timestamp === v.timestamp) delete last.timestamp

            if (unitLogs.unshift(v) >= MaxUnitLogLength) unitLogs.splice(MaxUnitLogLength)

            if (!treeViewRefreshTimeout) treeViewRefreshTimeout = setTimeout(() => {
                unitLogTreeDataProvider.refresh()
                treeViewRefreshTimeout = null
            }, 500)
        } else {
            log.trace('[Debugger] Custom event received:', e)
        }
    })

    log.info('[Debugger] Activated')
}

export function deactivate() {

}
