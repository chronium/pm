export interface DirtyRoute {
  canDeactivate(): boolean | Promise<boolean>;
}

export function canLeaveDirtyRoute(component: DirtyRoute): boolean | Promise<boolean> {
  return component.canDeactivate();
}
