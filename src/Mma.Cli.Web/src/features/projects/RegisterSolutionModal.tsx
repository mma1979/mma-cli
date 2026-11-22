import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../api/client";
import { errorMessage } from "../../api/problem";
import { queryKeys } from "../../api/queryKeys";
import type { RegisterWorkspaceResult } from "../../api/types";
import { Dialog } from "../../components/primitives/dialog";
import { Button } from "../../components/primitives/button";
import { Input } from "../../components/primitives/input";
import { ErrorBanner } from "../../components/shared/ErrorBanner";
import { useToast } from "../../components/primitives/toast";
import { truncateMiddle } from "../../lib/format";
import { DirectoryPickerModal } from "./DirectoryPickerModal";

export interface RegisterSolutionModalProps {
  open: boolean;
  onClose: () => void;
  onRegistered: (workspaceId: number) => void;
}

export function RegisterSolutionModal({ open, onClose, onRegistered }: RegisterSolutionModalProps) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [solutionPath, setSolutionPath] = useState("");
  const [pickerOpen, setPickerOpen] = useState(false);

  const register = useMutation({
    mutationFn: () =>
      api.post<RegisterWorkspaceResult>("/api/v1/workspaces/register", { solutionPath }),
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.workspaces });
      toast.push(`Registered ${result.workspace.name}`, "success");
      onRegistered(result.workspace.id);
      onClose();
    }
  });

  return (
    <>
      <Dialog
        open={open}
        onClose={onClose}
        title="Register an existing solution"
        description="Point MMA at a folder or an existing .sln/.slnx file."
        testId="register-solution-modal"
        footer={
          <>
            <Button variant="outline" onClick={onClose} disabled={register.isPending}>
              Cancel
            </Button>
            <Button
              data-testid="register-solution-submit"
              disabled={!solutionPath || register.isPending}
              onClick={() => register.mutate()}
            >
              {register.isPending ? "Registering…" : "Register"}
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <Input
              readOnly
              value={solutionPath ? truncateMiddle(solutionPath, 52) : ""}
              placeholder="No solution selected"
              title={solutionPath}
              data-testid="register-solution-path"
            />
            <Button variant="outline" onClick={() => setPickerOpen(true)}>
              Browse
            </Button>
          </div>

          {register.isError ? (
            <ErrorBanner
              message="Could not register the solution"
              detail={errorMessage(register.error)}
            />
          ) : null}
        </div>
      </Dialog>

      <DirectoryPickerModal
        open={pickerOpen}
        title="Choose a solution or folder"
        allowSolutionFiles
        onClose={() => setPickerOpen(false)}
        onSelect={(selection) => {
          setSolutionPath(selection.path);
          setPickerOpen(false);
        }}
      />
    </>
  );
}
