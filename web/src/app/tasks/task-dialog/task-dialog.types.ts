export interface DirtyDialogRoute {
  canDeactivate(): boolean | Promise<boolean>;
}

export function canLeaveDirtyDialog(component: DirtyDialogRoute): boolean | Promise<boolean> {
  return component.canDeactivate();
}
