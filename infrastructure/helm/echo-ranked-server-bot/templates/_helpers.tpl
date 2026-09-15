{{- define "echo-ranked-server-bot.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "echo-ranked-server-bot.fullname" -}}
{{- default (include "echo-ranked-server-bot.name" .) .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "echo-ranked-server-bot.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ include "echo-ranked-server-bot.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "echo-ranked-server-bot.selectorLabels" -}}
app.kubernetes.io/name: {{ include "echo-ranked-server-bot.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "echo-ranked-server-bot.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "echo-ranked-server-bot.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}
